using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

internal sealed class Settings
{
    public int MultiClickMs { get; set; }
    public int DoubleClickMs { get; set; }
    public int TripleClickMs { get; set; }
    public int LongPressMs { get; set; }
    public bool SeparateTripleGap { get; set; }
    public string Language { get; set; }
    public List<string> OutputDeviceIds { get; set; }
    public bool SwitchCommunications { get; set; }
    public string DeviceMatch { get; set; }
    public string SingleAction { get; set; }
    public string DoubleAction { get; set; }
    public string TripleAction { get; set; }
    public string LongAction { get; set; }

    public Settings Clone() { var copy = (Settings)MemberwiseClone(); copy.OutputDeviceIds = new List<string>(OutputDeviceIds ?? new List<string>()); return copy; }
    public void Normalize()
    {
        if (Language != "ko") Language = "en";
        if (OutputDeviceIds == null) OutputDeviceIds = new List<string>();
        OutputDeviceIds = OutputDeviceIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (string.IsNullOrWhiteSpace(DeviceMatch)) DeviceMatch = "VID_05AC&PID_022C&MI_01";
        if (DoubleClickMs == 0) DoubleClickMs = MultiClickMs > 0 ? MultiClickMs : 320;
        if (TripleClickMs == 0) TripleClickMs = DoubleClickMs;
        if (!SeparateTripleGap) TripleClickMs = DoubleClickMs;
        MultiClickMs = DoubleClickMs;
        if (LongPressMs == 0) LongPressMs = 600;
        if (string.IsNullOrEmpty(SingleAction)) SingleAction = "PlayPause";
        if (string.IsNullOrEmpty(DoubleAction)) DoubleAction = "NextTrack";
        if (string.IsNullOrEmpty(TripleAction)) TripleAction = "PreviousTrack";
        if (string.IsNullOrEmpty(LongAction)) LongAction = "SwitchOutput";
        if (DoubleClickMs < 120 || DoubleClickMs > 1000 || TripleClickMs < 120 || TripleClickMs > 1000 || LongPressMs < 150 || LongPressMs > 3000)
            throw new InvalidDataException(Ui.T("Click windows must be 120–1000ms; hold must be 150–3000ms.", "클릭 간격은 120~1000ms, 롱클릭은 150~3000ms 범위여야 합니다."));
        foreach (var action in new[] { SingleAction, DoubleAction, TripleAction, LongAction })
            if (!Actions.Names.ContainsKey(action)) throw new InvalidDataException(Ui.T("Unsupported action: ", "지원하지 않는 동작입니다: ") + action);
    }
    public string ActionFor(ClickGesture gesture)
    {
        return gesture == ClickGesture.Single ? SingleAction : gesture == ClickGesture.Double ? DoubleAction :
            gesture == ClickGesture.Triple ? TripleAction : LongAction;
    }
    public static Settings Load(string path)
    {
        var value = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));
        value.Normalize(); return value;
    }
    public void Save(string path)
    {
        Normalize();
        string temp = path + ".tmp";
        File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), new System.Text.UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
    }
}

internal static class Actions
{
    internal static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
        { "PlayPause", "Play / pause" }, { "NextTrack", "Next track" }, { "PreviousTrack", "Previous track" },
        { "SwitchOutput", "Cycle audio outputs" }, { "Mute", "Mute / unmute" }, { "None", "No action" }
    };
    internal static string Label(string action)
    {
        if (Ui.Lang != "ko") return Names[action];
        return action == "PlayPause" ? "재생 / 일시정지" : action == "NextTrack" ? "다음 곡" : action == "PreviousTrack" ? "이전 곡" : action == "SwitchOutput" ? "출력 장치 전환" : action == "Mute" ? "음소거 / 해제" : "동작 없음";
    }
    internal static string GestureName(ClickGesture gesture)
    {
        return gesture == ClickGesture.Single ? Ui.T("Single", "한 번") : gesture == ClickGesture.Double ? Ui.T("Double", "두 번") :
            gesture == ClickGesture.Triple ? Ui.T("Triple", "세 번") : Ui.T("Hold", "길게");
    }
}

internal sealed class Measurement
{
    public Measurement() { }
    public ClickGesture? Expected;
    public List<ClickGesture> Detected = new List<ClickGesture>();
    public List<long> Holds = new List<long>();
    public List<long> Gaps = new List<long>();
    public string Source;
    public long Start, LastDown, LastUp, DecisionAt;
    public bool Incomplete;
    public string Command = "Practice";
    public long CommandMs = -1;
    internal void BeginPress(long now)
    {
        if (LastDown == 0 && Holds.Count == 0) Start = now;
        if (Holds.Count > 0) Gaps.Add(now - LastUp);
        LastDown = now;
    }
    internal bool Passed
    {
        get { return !Incomplete && (!Expected.HasValue || (Detected.Count == 1 && Detected[0] == Expected.Value)); }
    }
    internal int TargetCount
    {
        get { return Expected == ClickGesture.Double ? 2 : Expected == ClickGesture.Triple ? 3 : 1; }
    }
    internal string DetectionText
    {
        get { return Detected.Count == 0 ? (Incomplete ? Ui.T("Incomplete", "측정 미완료") : Ui.T("Pending", "판정 대기")) : string.Join(" + ", Detected.Select(Actions.GestureName)); }
    }
}

internal static class Calibration
{
    internal static Settings Recommend(Settings current, IEnumerable<Measurement> records, out string explanation)
    {
        var physical = records.Where(x => x.Source == "VK01" && x.Expected.HasValue && !x.Incomplete && x.Holds.Count == x.TargetCount).ToList();
        var taps = physical.Where(x => x.Expected != ClickGesture.Long).SelectMany(x => x.Holds).ToList();
        var doubleGaps = physical.Where(x => x.Expected == ClickGesture.Double || x.Expected == ClickGesture.Triple)
            .Where(x => x.Gaps.Count > 0).Select(x => x.Gaps[0]).ToList();
        var tripleGaps = physical.Where(x => x.Expected == ClickGesture.Triple).Where(x => x.Gaps.Count > 1).Select(x => x.Gaps[1]).ToList();
        if (taps.Count < 5 || doubleGaps.Count + tripleGaps.Count < 3)
            throw new InvalidOperationException(Ui.T("Measure single, double and triple on your VK01 first. At least 5 taps and 3 gaps with a labeled intent are needed.", "실제 VK01으로 한 번·두 번·세 번 클릭을 각각 측정해 주세요. 짧은 누름 5개와 클릭 간격 3개 이상이 필요합니다."));
        Settings proposed = current.Clone();
        proposed.DoubleClickMs = Rounded(Percentile(doubleGaps, .95) + 60, 180, 700);
        proposed.TripleClickMs = proposed.SeparateTripleGap && tripleGaps.Count > 0 ? Rounded(Percentile(tripleGaps, .95) + 60, 180, 700) : proposed.DoubleClickMs;
        if (!proposed.SeparateTripleGap) proposed.DoubleClickMs = proposed.TripleClickMs = Rounded(Percentile(doubleGaps.Concat(tripleGaps).ToList(), .95) + 60, 180, 700);
        proposed.LongPressMs = Rounded(Percentile(taps, .95) + 200, 350, 1500);
        var holds = physical.Where(x => x.Expected == ClickGesture.Long).SelectMany(x => x.Holds).ToList();
        if (holds.Count > 0 && holds.Min() <= proposed.LongPressMs)
        {
            if (holds.Min() <= taps.Max() + 100) throw new InvalidOperationException(Ui.T("Tap and hold times overlap. Choose the intended gesture and measure again.", "짧은 누름과 긴 누름의 시간이 겹칩니다. 의도한 동작을 선택하고 다시 측정해 주세요."));
            proposed.LongPressMs = Rounded((holds.Min() + taps.Max()) / 2, 150, 1500);
        }
        proposed.Normalize();
        explanation = Ui.Lang == "ko" ? "짧은 누름 " + taps.Count + "개, 클릭 간격 " + (doubleGaps.Count + tripleGaps.Count) + "개를 사용했습니다. 클릭 간격의 95백분위에 60ms 여유, 짧은 누름에 200ms 여유를 더한 제안입니다. 적용 전 연습할 수 있습니다." :
            "Based on " + taps.Count + " taps and " + (doubleGaps.Count + tripleGaps.Count) + " gaps. Suggested windows add 60ms to the 95th-percentile gap and 200ms to taps. Practice before saving.";
        return proposed;
    }
    private static double Percentile(List<long> values, double p)
    {
        if (values.Count == 0) return 300;
        var sorted = values.OrderBy(x => x).ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * p) - 1)];
    }
    private static int Rounded(double value, int min, int max) { return Math.Max(min, Math.Min(max, (int)(Math.Ceiling(value / 10) * 10))); }
}
