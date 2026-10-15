using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

internal static class SettingsTests
{
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private static void Main()
    {
        var value = new Settings { MultiClickMs = 360, LongPressMs = 1000, DeviceMatch = "VID_05AC&PID_022C&MI_01" };
        value.Normalize(); Check(value.DoubleClickMs == 360 && value.TripleClickMs == 360 && value.LongPressMs == 1000, "legacy timing is preserved");
        Check(value.SingleAction == "PlayPause" && value.LongAction == "SwitchOutput", "legacy actions are preserved");
        Check(value.Language == "en", "new and migrated profiles default to English");
        Check(value.ClickInput == KnobInput.Keyboard, "existing profiles retain the F13 input path");
        value.OutputDeviceIds.AddRange(new[] { "endpoint-a", "endpoint-b" });
        var independent = value.Clone(); independent.OutputDeviceIds.RemoveAt(0);
        Check(value.OutputDeviceIds.Count == 2 && independent.OutputDeviceIds.Count == 1, "draft output order does not mutate saved selections");
        var trial = new Measurement { Start = 100, Expected = ClickGesture.Double };
        trial.BeginPress(2000); trial.Holds.Add(40); trial.LastUp = 2040; trial.BeginPress(2220); trial.DecisionAt = 2600;
        Check(trial.Start == 2000 && trial.Gaps[0] == 180 && trial.DecisionAt - trial.Start == 600, "trial timing excludes time spent waiting to start");
        var records = new List<Measurement>();
        var single = new Measurement { Source = "VK01", Expected = ClickGesture.Single }; single.Holds.Add(30); single.Detected.Add(ClickGesture.Single); records.Add(single);
        var twice = new Measurement { Source = "VK01", Expected = ClickGesture.Double }; twice.Holds.AddRange(new long[] { 40, 35 }); twice.Gaps.Add(180); twice.Detected.Add(ClickGesture.Double); records.Add(twice);
        var triple = new Measurement { Source = "VK01", Expected = ClickGesture.Triple }; triple.Holds.AddRange(new long[] { 25, 30, 40 }); triple.Gaps.AddRange(new long[] { 200, 210 }); triple.Detected.Add(ClickGesture.Triple); records.Add(triple);
        string why; var proposed = Calibration.Recommend(value, records, out why);
        Check(proposed.DoubleClickMs == 270 && proposed.TripleClickMs == 270 && proposed.LongPressMs == 350, "calibration adds measured safety margins");
        Check(value.DoubleClickMs == 360 && value.LongPressMs == 1000, "recommendation does not mutate saved timing");
        var serializer = new JavaScriptSerializer(); var restored = serializer.Deserialize<List<Measurement>>(serializer.Serialize(records));
        Check(restored.Count == 3 && restored[2].Gaps.Count == 2 && restored[1].Expected == ClickGesture.Double, "measurement history round trips");
        foreach (var record in records) record.Source = "화면";
        bool rejected = false; try { Calibration.Recommend(value, records, out why); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "screen practice cannot train physical calibration");
        string file = Path.Combine(Path.GetTempPath(), "vk01-settings-test-" + Guid.NewGuid().ToString("N") + ".json");
        try { value.Save(file); proposed.ClickInput = KnobInput.Receiver; proposed.Save(file); var loaded = Settings.Load(file); Check(loaded.LongPressMs == 350 && File.Exists(file + ".bak"), "atomic save keeps the previous settings backup"); Check(loaded.OutputDeviceIds[1] == "endpoint-b" && loaded.Language == "en", "output order and language survive save and reload"); Check(loaded.ClickInput == KnobInput.Receiver, "receiver mode survives save and reload"); }
        finally { File.Delete(file); if (File.Exists(file + ".bak")) File.Delete(file + ".bak"); }
        value.ClickInput = "AnyKeyboard"; bool invalidInput = false;
        try { value.Normalize(); } catch (InvalidDataException) { invalidInput = true; }
        Check(invalidInput, "unknown input modes are rejected");
    }
}
