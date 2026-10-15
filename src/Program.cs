using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Program
{
    internal static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VK01Studio");
    internal static void Log(string message)
    {
        try
        {
            lock (typeof(Program))
            {
                string file = Path.Combine(Root, "activity.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1048576)
                    File.Move(file, Path.Combine(Root, "activity-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".log"));
                File.AppendAllText(file, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine);
            }
        }
        catch { }
    }
    [STAThread]
    private static void Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        if (Array.IndexOf(args, "--install-startup") >= 0 || Array.IndexOf(args, "--remove-startup") >= 0)
        {
            Startup.Set(Array.IndexOf(args, "--remove-startup") < 0);
            return;
        }
        bool created;
        using (var mutex = new Mutex(true, @"Local\VK01ClickControl", out created))
        {
            if (!created)
            {
                try { using (var signal = EventWaitHandle.OpenExisting(@"Local\VK01ClickControl.ShowSettings")) signal.Set(); } catch { }
                return;
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                string settingsPath = Path.Combine(Root, "settings.json");
                var settings = File.Exists(settingsPath) ? Settings.Load(settingsPath) : new Settings(); settings.Normalize();
                Ui.Lang = settings.Language;
                using (var context = new Controller(settings))
                {
                    if (Array.IndexOf(args, "--background") < 0) context.ShowSettings();
                    Application.Run(context);
                }
            }
            catch (Exception e) { Log("FATAL " + e); MessageBox.Show(e.Message, "VK01 Click Control", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { mutex.ReleaseMutex(); }
        }
    }
}

internal sealed class ViewState
{
    internal bool Down, LongFired, Cancelled, Practice, Measuring, Connected;
    internal long HoldMs, WaitMs;
    internal int Pending, LongMs, DoubleMs, TripleMs, TrialCount, TrialTarget;
    internal string Status;
}

internal sealed class WorkItem
{
    internal string Action; internal Settings Settings; internal Measurement Measurement;
    internal ClickGesture Gesture; internal string Outcome; internal long Duration;
}

internal sealed class Controller : ApplicationContext
{
    internal event Action<Measurement> Recorded;
    internal Settings Saved { get; private set; }
    private Settings live;
    private ClickDetector detector;
    private readonly InputWindow input;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer;
    private readonly EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\VK01ClickControl.ShowSettings");
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Queue<WorkItem> queue = new Queue<WorkItem>();
    private readonly BackgroundWorker worker = new BackgroundWorker();
    private SettingsWindow form;
    private Measurement current, trial;
    private long pressedAt, trialFinishAt, trialDeadline;
    private long nextDeviceCheck;
    private bool connected;
    private bool practice, paused, shuttingDown, suppressUntilUp;
    private string status = Ui.T("Press your VK01 to see live input", "VK01을 눌러 입력을 확인하세요");

    internal Controller(Settings settings)
    {
        Saved = settings.Clone(); live = settings.Clone(); Rebuild();
        input = new InputWindow(this);
        System.Drawing.Icon appIcon;
        using (var stream = typeof(Program).Assembly.GetManifestResourceStream("app.ico")) appIcon = new System.Drawing.Icon(stream);
        tray = new NotifyIcon { Icon = appIcon, Text = "VK01 Studio", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add(Ui.T("Open VK01 Studio", "VK01 Studio 열기"), null, delegate { ShowSettings(); });
        menu.Items.Add(new ToolStripSeparator());
        var pause = new ToolStripMenuItem(Ui.T("Pause input", "입력 일시 중지")) { CheckOnClick = true };
        pause.CheckedChanged += delegate { paused = pause.Checked; ResetInput(); status = paused ? Ui.T("Input paused", "입력 일시 중지됨") : Ui.T("Ready", "입력 대기"); };
        menu.Items.Add(pause); menu.Items.Add(Ui.T("Exit", "종료"), null, delegate { ExitThread(); });
        tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowSettings(); };
        worker.DoWork += Execute;
        worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null) Program.Log("ACTION ERROR " + e.Error.Message);
            var item = e.Error == null ? e.Result as WorkItem : null;
            if (item != null) { item.Measurement.Command = item.Outcome; item.Measurement.CommandMs = item.Duration; }
            if (!shuttingDown) StartNext();
        };
        timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += delegate
        {
            if (showSignal.WaitOne(0)) ShowSettings();
            long now = clock.ElapsedMilliseconds;
            if (now >= nextDeviceCheck) { nextDeviceCheck = now + 2000; connected = input.HasTarget(); }
            if (!paused && !suppressUntilUp) detector.Tick(now);
            if (trial != null && ((trialFinishAt > 0 && now >= trialFinishAt) || now >= trialDeadline)) FinishTrial();
        };
        timer.Start();
        Program.Log("READY ui_version=3 click_input=" + live.ClickInput + " double_ms=" + live.DoubleClickMs + " triple_ms=" + live.TripleClickMs + " long_ms=" + live.LongPressMs);
    }
    private void Rebuild()
    {
        if (detector != null && detector.IsDown) suppressUntilUp = true;
        detector = new ClickDetector(live.DoubleClickMs, live.TripleClickMs, live.LongPressMs, OnGesture);
        current = trial = null; trialFinishAt = 0;
    }
    private void ResetInput()
    {
        if (detector.IsDown) suppressUntilUp = true;
        detector.Reset(); current = trial = null; trialFinishAt = 0;
    }
    internal void ShowSettings()
    {
        if (form != null && form.IsVisible) { form.WindowState = System.Windows.WindowState.Normal; form.Activate(); return; }
        practice = true; live = Saved.Clone(); Rebuild();
        form = new SettingsWindow(this); System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(form); form.Show(); form.Activate();
    }
    internal void CloseSettings() { practice = false; live = Saved.Clone(); Rebuild(); status = Ui.T("Running with saved settings", "저장된 설정으로 동작 중"); }
    internal void Preview(Settings value) { value.Normalize(); live = value.Clone(); Rebuild(); status = Ui.T("Preview updated · try your VK01", "새 기준으로 연습할 수 있습니다"); }
    internal void SetLanguage(string language)
    {
        var value = Saved.Clone(); value.Language = language; value.Save(Path.Combine(Program.Root, "settings.json")); Saved = value; live.Language = language; Ui.Lang = language;
        tray.ContextMenuStrip.Items[0].Text = Ui.T("Open VK01 Studio", "VK01 Studio 열기");
        tray.ContextMenuStrip.Items[2].Text = Ui.T("Pause input", "입력 일시 중지"); tray.ContextMenuStrip.Items[3].Text = Ui.T("Exit", "종료");
        status = Ui.T("Language updated", "언어를 변경했습니다");
    }
    internal void Apply(Settings value)
    {
        value.Normalize(); value.Save(Path.Combine(Program.Root, "settings.json")); Saved = value.Clone(); live = value.Clone(); Rebuild();
        status = Ui.T("Saved · changes are now active", "저장 완료 · 재시작 없이 적용됨");
        Program.Log("SETTINGS_APPLIED click_input=" + live.ClickInput + " double_ms=" + live.DoubleClickMs + " triple_ms=" + live.TripleClickMs + " long_ms=" + live.LongPressMs);
    }
    internal void SetPractice(bool value) { practice = value; ResetInput(); status = value ? Ui.T("Practice mode · no actions sent", "연습 모드 · 실제 동작 없음") : Ui.T("Live mode · actions enabled", "실제 동작 실행 중"); }
    internal void StartTrial(ClickGesture expected)
    {
        ResetInput(); trial = new Measurement { Expected = expected, Start = clock.ElapsedMilliseconds, Command = "Measure only", Source = "" };
        trialDeadline = trial.Start + 12000;
        status = Ui.T("Measuring: ", "측정 중: ") + Actions.GestureName(expected) + Ui.T(" · use your VK01 within 12 seconds", " · 12초 안에 실제 VK01을 누르세요");
    }
    internal ViewState State
    {
        get
        {
            long now = clock.ElapsedMilliseconds;
            return new ViewState { Connected = connected, Down = detector.IsDown, LongFired = detector.LongFired, Cancelled = detector.Cancelled,
                HoldMs = detector.HoldMs(now), WaitMs = detector.WaitMs(now), Pending = detector.PendingCount,
                LongMs = live.LongPressMs, DoubleMs = live.DoubleClickMs, TripleMs = live.TripleClickMs,
                Practice = practice || trial != null, Measuring = trial != null, TrialCount = trial == null ? 0 : trial.Holds.Count,
                TrialTarget = trial == null ? 0 : trial.TargetCount, Status = status };
        }
    }
    internal void TargetKey(ushort key, bool up)
    {
        if (key == 0x7c && live.ClickInput == KnobInput.Keyboard) Receive(up, "VK01"); else if (!up && !paused) Rotation();
    }
    internal void Simulate(bool up) { if (practice || trial != null) Receive(up, "Screen"); }
    private void Receive(bool up, string source)
    {
        if (paused) return;
        if (suppressUntilUp) { if (up) suppressUntilUp = false; return; }
        long now = clock.ElapsedMilliseconds;
        if (!up)
        {
            if (detector.IsDown) return;
            detector.Tick(now);
            if (trial != null)
            {
                if (trial.Source != "" && trial.Source != source) { trial.Incomplete = true; FinishTrial(); return; }
                trial.Source = source;
                trial.BeginPress(now);
            }
            if (current == null || detector.PendingCount == 0) current = new Measurement { Start = now, Source = source };
            else current.Gaps.Add(now - current.LastUp);
            pressedAt = now; current.LastDown = now;
            detector.Down(now); status = Ui.T("Holding…", "누르는 중"); Program.Log("INPUT " + live.ClickInput + " DOWN source=" + source);
        }
        else
        {
            if (!detector.IsDown) return;
            long duration = now - pressedAt;
            if (current != null) { current.Holds.Add(duration); current.LastUp = now; }
            if (trial != null)
            {
                trial.Holds.Add(duration); trial.LastUp = now;
                if (trial.Holds.Count >= trial.TargetCount) trialFinishAt = now + Math.Max(live.DoubleClickMs, live.TripleClickMs) + 70;
            }
            Program.Log("INPUT " + live.ClickInput + " UP source=" + source + " held_ms=" + duration);
            detector.Up(now);
            if (detector.PendingCount == 0) current = null;
            status = detector.PendingCount > 0 ? Ui.T("Waiting for another click", "다음 클릭을 기다리는 중") : Ui.T("Input complete", "입력 완료");
        }
    }
    internal void TargetHid(byte[] bytes)
    {
        KnobSignal signal = KnobInput.Consumer(bytes, live.ClickInput);
        if (signal == KnobSignal.Press) Receive(false, "VK01");
        else if (signal == KnobSignal.Release) Receive(true, "VK01");
        else if (signal == KnobSignal.Rotation && !paused) Rotation();
    }
    private void Rotation()
    {
        detector.Rotation(); status = Ui.T("Rotation detected · pending gesture cancelled", "회전 감지 · 진행 중인 클릭 판정 취소");
        if (trial != null) { trial.Incomplete = true; FinishTrial(); }
    }
    internal void Removed() { ResetInput(); suppressUntilUp = false; status = Ui.T("VK01 disconnected · reconnect to continue", "VK01 연결 해제 · 다시 연결해 주세요"); Program.Log("DEVICE_REMOVED"); }
    internal bool Matches(string name) { return name.IndexOf(live.DeviceMatch, StringComparison.OrdinalIgnoreCase) >= 0; }
    private void FinishTrial()
    {
        Measurement finished = trial; if (finished == null) return;
        finished.Incomplete = finished.Incomplete || detector.IsDown || finished.Holds.Count != finished.TargetCount;
        trial = null; current = null; trialFinishAt = 0;
        if (detector.IsDown) suppressUntilUp = true;
        detector.Reset(); if (Recorded != null) Recorded(finished);
        status = finished.Passed ? Ui.T("Matched · intended gesture detected", "측정 완료 · 의도와 판정이 일치합니다") : Ui.T("Measurement complete · review the result", "측정 완료 · 아래 기록에서 차이를 확인하세요");
    }
    private void OnGesture(ClickGesture gesture)
    {
        long now = clock.ElapsedMilliseconds;
        Program.Log((practice || trial != null ? "TEST " : "GESTURE ") + gesture + " elapsed_ms=" + (now - pressedAt));
        if (trial != null) { trial.Detected.Add(gesture); trial.DecisionAt = now; return; }
        Measurement measured = current ?? new Measurement { Start = pressedAt, Source = "VK01" };
        measured.Detected.Add(gesture); measured.DecisionAt = now; measured.Command = practice ? "Practice" : "Queued";
        if (Recorded != null) Recorded(measured);
        if (!practice) { queue.Enqueue(new WorkItem { Gesture = gesture, Action = live.ActionFor(gesture), Settings = live.Clone(), Measurement = measured }); StartNext(); }
        if (!detector.IsDown) current = null;
        status = Actions.GestureName(gesture) + Ui.T(" detected", " 클릭으로 판정");
    }
    private void StartNext() { if (!worker.IsBusy && queue.Count > 0) worker.RunWorkerAsync(queue.Dequeue()); }
    private void Execute(object sender, DoWorkEventArgs e)
    {
        var item = (WorkItem)e.Argument; var elapsed = Stopwatch.StartNew();
        try
        {
            if (item.Action == "None") item.Outcome = "No action";
            else if (item.Action == "SwitchOutput")
            {
                item.Outcome = AudioDevices.Cycle(item.Settings);
            }
            else
            {
                ushort key = item.Action == "NextTrack" ? (ushort)0xb0 : item.Action == "PreviousTrack" ? (ushort)0xb1 : item.Action == "Mute" ? (ushort)0xad : (ushort)0xb3;
                if (!Native.SendMedia(key)) throw new Win32Exception(Marshal.GetLastWin32Error());
                item.Outcome = "Media command sent";
            }
            Program.Log("ACTION " + item.Gesture + " " + item.Action + " OK command_ms=" + elapsed.ElapsedMilliseconds);
        }
        catch (Exception ex) { item.Outcome = "Error: " + ex.Message; Program.Log("ACTION ERROR " + ex.Message); }
        item.Duration = elapsed.ElapsedMilliseconds; e.Result = item;
    }
    protected override void ExitThreadCore()
    {
        shuttingDown = true; timer.Stop(); detector.Reset(); tray.Visible = false;
        if (form != null && form.IsVisible) form.Close();
        input.Dispose(); tray.Dispose(); timer.Dispose(); showSignal.Dispose(); Program.Log("STOP"); base.ExitThreadCore();
    }
}

internal sealed class InputWindow : NativeWindow, IDisposable
{
    private readonly Controller controller;
    private readonly Dictionary<IntPtr, string> names = new Dictionary<IntPtr, string>();
    internal InputWindow(Controller controller)
    {
        this.controller = controller; CreateHandle(new CreateParams { Caption = "VK01 input", Parent = new IntPtr(-3) });
        var devices = new[] { new Native.RawDevice { Page = 1, Usage = 6, Flags = 0x2100, Target = Handle }, new Native.RawDevice { Page = 0x0c, Usage = 1, Flags = 0x2100, Target = Handle } };
        if (!Native.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(Native.RawDevice)))) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private string Name(IntPtr handle)
    {
        string name; if (names.TryGetValue(handle, out name)) return name;
        uint length = 0; Native.GetRawInputDeviceInfo(handle, 0x20000007, null, ref length);
        if (length == 0 || length > 32768) return string.Empty;
        var text = new StringBuilder((int)length + 1); Native.GetRawInputDeviceInfo(handle, 0x20000007, text, ref length);
        name = text.ToString(); names[handle] = name; return name;
    }
    internal bool HasTarget()
    {
        uint count = 0, size = (uint)Marshal.SizeOf(typeof(Native.RawDeviceEntry));
        if (Native.GetRawInputDeviceList(IntPtr.Zero, ref count, size) == uint.MaxValue || count == 0 || count > 1024) return false;
        IntPtr buffer = Marshal.AllocHGlobal((int)(count * size));
        try
        {
            uint received = Native.GetRawInputDeviceList(buffer, ref count, size);
            if (received == uint.MaxValue) return false;
            for (int i = 0; i < received; i++)
            {
                var entry = (Native.RawDeviceEntry)Marshal.PtrToStructure(IntPtr.Add(buffer, (int)(i * size)), typeof(Native.RawDeviceEntry));
                if (entry.Type == 1 && controller.Matches(Name(entry.Device))) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    protected override void WndProc(ref Message message)
    {
        try
        {
            if (message.Msg == 0xfe) { if (message.WParam.ToInt32() == 2 && controller.Matches(Name(message.LParam))) controller.Removed(); names.Remove(message.LParam); }
            else if (message.Msg == 0xff) ReadInput(message.LParam);
        }
        catch (Exception ex) { Program.Log("INPUT ERROR " + ex.Message); }
        base.WndProc(ref message);
    }
    private void ReadInput(IntPtr input)
    {
        uint size = 0, headerSize = (uint)Marshal.SizeOf(typeof(Native.RawHeader));
        Native.GetRawInputData(input, 0x10000003, IntPtr.Zero, ref size, headerSize);
        if (size < headerSize || size > 65536) return;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Native.GetRawInputData(input, 0x10000003, buffer, ref size, headerSize) == uint.MaxValue) return;
            var header = (Native.RawHeader)Marshal.PtrToStructure(buffer, typeof(Native.RawHeader)); if (!controller.Matches(Name(header.Device))) return;
            IntPtr data = IntPtr.Add(buffer, (int)headerSize);
            if (header.Type == 1 && size >= headerSize + 16) controller.TargetKey((ushort)Marshal.ReadInt16(data, 6), (Marshal.ReadInt16(data, 2) & 1) != 0);
            else if (header.Type == 2 && size >= headerSize + 8)
            {
                int length = Marshal.ReadInt32(data, 0), count = Marshal.ReadInt32(data, 4);
                if (length <= 0 || count <= 0 || (long)length * count > size - headerSize - 8) return;
                for (int i = 0; i < count; i++) { var bytes = new byte[length]; Marshal.Copy(IntPtr.Add(data, 8 + i * length), bytes, 0, bytes.Length); controller.TargetHid(bytes); }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public void Dispose() { DestroyHandle(); }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawHeader { public uint Type, Size; public IntPtr Device, WParam; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawDeviceEntry { public IntPtr Device; public uint Type; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);
    [DllImport("user32.dll")] internal static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    private static Input Key(ushort key, bool up) { return new Input { Type = 1, Data = new InputUnion { Keyboard = new Keyboard { Vk = key, Flags = up ? 2u : 0u } } }; }
    internal static bool SendMedia(ushort key) { return SendInput(2, new[] { Key(key, false), Key(key, true) }, Marshal.SizeOf(typeof(Input))) == 2; }
}
