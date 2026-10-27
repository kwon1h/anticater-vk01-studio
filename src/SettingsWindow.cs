using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal sealed class SettingsWindow : Window
{
    private readonly Controller controller;
    private Settings draft;
    private string page = "controls";
    private bool practice, building;
    private readonly List<Measurement> records = new List<Measurement>();
    private readonly DispatcherTimer timer;
    private readonly string historyPath = Path.Combine(Program.Root, "measurements.json");
    private TextBlock status, feedback, pending, connection, progressTitle, progressHint, savedNotice;
    private Button save;
    private ProgressRing ring;
    private readonly ImageSource brandIcon;
    private DataGrid history;
    private StackPanel audioList, cycleList;
    private string audioSignature = "", rowsSignature = "";
    private DateTime nextRefresh = DateTime.MinValue;
    private List<OutputDevice> devices = new List<OutputDevice>();
    private const string Ink = "#F3F4FB", Muted = "#A6AFC7", Accent = "#B8A4FF", Good = "#73E2C2";

    internal SettingsWindow(Controller controller)
    {
        this.controller = controller; draft = controller.Saved.Clone();
        Title = "VK01 Studio"; Width = 1180; Height = 850; MinWidth = 1000; MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brush("#10121B");
        Foreground = Brush(Ink); FontFamily = new FontFamily("Segoe UI, Malgun Gothic"); FontSize = 13;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        using (var stream = typeof(Program).Assembly.GetManifestResourceStream("Theme.xaml")) Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
        using (var stream = typeof(Program).Assembly.GetManifestResourceStream("app.ico")) Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        using (var stream = typeof(Program).Assembly.GetManifestResourceStream("icon.png")) brandIcon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        SourceInitialized += delegate { int enabled = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref enabled, 4); };
        LoadRecords(); controller.Recorded += Recorded;
        Build();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += delegate { UpdateLive(); }; timer.Start();
        Closed += delegate { timer.Stop(); SaveRecords(); controller.Recorded -= Recorded; controller.CloseSettings(); };
    }
    private static SolidColorBrush Brush(string value) { return (SolidColorBrush)new BrushConverter().ConvertFromString(value); }
    private static string T(string en, string ko) { return Ui.T(en, ko); }
    private static TextBlock Text(string value, double size, string color)
    {
        return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    }
    private static StackPanel Stack() { return new StackPanel(); }
    private static TextBlock Heading(string value) { var text = Text(value, 17, Ink); text.FontWeight = FontWeights.SemiBold; text.Margin = new Thickness(0, 0, 0, 18); return text; }
    private static Border Card(UIElement child, Thickness margin)
    {
        return new Border { Background = Brush("#191D2B"), BorderBrush = Brush("#2B3245"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Margin = margin, Child = child };
    }
    private Button Button(string label, Action action, bool primary)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0) };
        if (primary) button.Style = (Style)Resources["Primary"];
        button.Click += delegate { try { action(); } catch (Exception ex) { Message(ex.Message, true); } };
        return button;
    }
    private CheckBox Switch(string label, bool value, Action<bool> change)
    {
        var toggle = new CheckBox { Content = label, Style = (Style)Resources["Switch"], IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        toggle.Checked += delegate { if (!building) change(true); }; toggle.Unchecked += delegate { if (!building) change(false); };
        return toggle;
    }
    private void Build()
    {
        building = true;
        ring = null; history = null; audioList = cycleList = null; progressTitle = progressHint = pending = status = null;
        var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(202) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var side = new Grid { Background = Brush("#151823"), Margin = new Thickness(0, 0, 0, 0) };
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); side.RowDefinitions.Add(new RowDefinition()); side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var brand = Stack(); brand.Margin = new Thickness(24, 30, 20, 30);
        var logoRow = new StackPanel { Orientation = Orientation.Horizontal };
        logoRow.Children.Add(new Image { Source = brandIcon, Width = 38, Height = 38, Margin = new Thickness(0, 0, 10, 0) });
        var brandType = Stack(); var brandName = Text("VK01", 22, Ink); brandName.FontWeight = FontWeights.Bold; brandType.Children.Add(brandName);
        brandType.Children.Add(Text("STUDIO", 10, Muted)); logoRow.Children.Add(brandType); brand.Children.Add(logoRow); side.Children.Add(brand);
        var nav = Stack(); nav.Margin = new Thickness(14, 0, 14, 0);
        Nav(nav, "controls", "\uE713", T("Controls", "조작 설정")); Nav(nav, "audio", "\uE767", T("Audio outputs", "출력 장치")); Nav(nav, "calibration", "\uE9D9", T("Calibration", "측정 및 연습"));
        Grid.SetRow(nav, 1); side.Children.Add(nav);
        var sideBottom = Stack(); sideBottom.Margin = new Thickness(20, 20, 18, 25);
        connection = Text("•  VK01", 12, Muted); sideBottom.Children.Add(connection);
        var startup = Switch(T("Start with Windows", "Windows 자동 시작"), Startup.Enabled, value => { Startup.Set(value); Build(); Message(value && Startup.BlockedByWindows ? T("Enable VK01 Studio in Windows startup apps.", "Windows 시작 앱에서 VK01 Studio를 켜주세요.") : value ? T("Autostart enabled", "자동 시작을 켰습니다") : T("Autostart disabled", "자동 시작을 껐습니다"), value && Startup.BlockedByWindows); });
        startup.Margin = new Thickness(0, 22, 0, 18); startup.FontSize = 11; sideBottom.Children.Add(startup);
        if (Startup.BlockedByWindows) { var openStartup = Button(T("Open Windows startup apps", "Windows 시작 앱 열기"), () => System.Diagnostics.Process.Start("ms-settings:startupapps"), false); openStartup.FontSize = 10; sideBottom.Children.Add(openStartup); }
        sideBottom.Children.Add(Text("ANTICATER VK01  /  v1.2.0", 10, Muted)); Grid.SetRow(sideBottom, 2); side.Children.Add(sideBottom); root.Children.Add(side);
        var main = new Grid { Margin = new Thickness(30, 24, 30, 22) };
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new Grid { Margin = new Thickness(0, 0, 0, 23) }; top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Text("YOUR KNOB. YOUR RULES.", 10, Muted));
        var language = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        language.Children.Add(Text("EN", 12, Ui.Lang == "en" ? Accent : Muted));
        var langSwitch = Switch("", Ui.Lang == "ko", value => { string lang = value ? "ko" : "en"; controller.SetLanguage(lang); draft.Language = lang; Build(); });
        AutomationProperties.SetName(langSwitch, "Language / 언어"); langSwitch.Margin = new Thickness(8, 0, 8, 0); language.Children.Add(langSwitch);
        language.Children.Add(Text("한국어", 12, Ui.Lang == "ko" ? Accent : Muted)); Grid.SetColumn(language, 1); top.Children.Add(language); main.Children.Add(top);
        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 23) }; titleRow.ColumnDefinitions.Add(new ColumnDefinition()); titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Stack(); var titleText = Text(page == "audio" ? T("Every output, one click away.", "출력 전환을 더 간편하게.") : page == "calibration" ? T("Find your rhythm.", "내 손의 리듬에 맞추세요.") : T("Make every click count.", "클릭마다 원하는 동작을."), 27, Ink); titleText.FontWeight = FontWeights.SemiBold; title.Children.Add(titleText);
        var subtitle = Text(page == "audio" ? T("Choose your devices. Set the order. Switch directly in Windows.", "장치를 선택하고 순서를 정하세요. Windows에서 직접 전환합니다.") : page == "calibration" ? T("Measure real input and tune the timing to your hand.", "실제 입력을 측정하고 클릭 시간을 내 손에 맞추세요.") : T("One knob for your music, audio and daily flow.", "노브 하나로 음악과 오디오를 편하게 제어하세요."), 12, Muted); subtitle.Margin = new Thickness(0, 7, 0, 0); title.Children.Add(subtitle); titleRow.Children.Add(title);
        var mode = Switch(T("Practice", "연습 모드"), practice, value => { practice = value; controller.SetPractice(value); controller.Preview(draft.Clone()); UpdateDirty(); }); mode.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(mode, 1); titleRow.Children.Add(mode);
        Grid.SetRow(titleRow, 1); main.Children.Add(titleRow);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 5, 0) };
        var content = Stack();
        if (page != "audio") content.Children.Add(Preview());
        if (page == "controls") ControlsPage(content); else if (page == "audio") AudioPage(content); else CalibrationPage(content);
        scroll.Content = content; Grid.SetRow(scroll, 2); main.Children.Add(scroll);
        var footer = new Grid { Margin = new Thickness(0, 20, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var footerText = Stack(); savedNotice = Text("", 12, Muted); footerText.Children.Add(savedNotice); feedback = Text("", 11, Good); feedback.Margin = new Thickness(0, 4, 10, 0); footerText.Children.Add(feedback); footer.Children.Add(footerText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button(T("Revert", "되돌리기"), () => { draft = controller.Saved.Clone(); controller.Preview(draft.Clone()); Build(); }, false));
        save = Button(T("Save changes", "저장 및 적용"), () => { controller.Apply(draft.Clone()); UpdateDirty(); Message(T("Saved. Ready for your next click.", "저장했습니다. 새 설정으로 동작합니다."), false); }, true); save.Margin = new Thickness(0); buttons.Children.Add(save); Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        Grid.SetRow(footer, 3); main.Children.Add(footer); Grid.SetColumn(main, 1); root.Children.Add(main); Content = root;
        building = false; UpdateDirty(); UpdateLive();
    }
    private void Nav(StackPanel panel, string key, string glyph, string name)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal }; label.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }); label.Children.Add(Text(name, 13, page == key ? Accent : Muted));
        var button = Button(name, () => { page = key; Build(); }, false); button.Content = label; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 0, 0, 8); button.Padding = new Thickness(15, 14, 10, 14); button.BorderThickness = new Thickness(0); button.Background = Brush(page == key ? "#2B2543" : "#151823"); AutomationProperties.SetName(button, name); panel.Children.Add(button);
    }
    private Border Preview()
    {
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        ring = new ProgressRing { Width = 100, Height = 100, Cursor = Cursors.Hand, Focusable = true };
        AutomationProperties.SetName(ring, T("Click practice pad", "화면 클릭 연습 영역"));
        ring.MouseLeftButtonDown += delegate { ring.Focus(); ring.CaptureMouse(); controller.Simulate(false); };
        ring.MouseLeftButtonUp += delegate { controller.Simulate(true); ring.ReleaseMouseCapture(); };
        ring.LostMouseCapture += delegate { controller.Simulate(true); };
        ring.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space) { controller.Simulate(false); e.Handled = true; } };
        ring.KeyUp += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space) { controller.Simulate(true); e.Handled = true; } };
        layout.Children.Add(ring);
        var info = Stack(); info.VerticalAlignment = VerticalAlignment.Center; progressTitle = Text("", 17, Ink); progressTitle.FontWeight = FontWeights.SemiBold; info.Children.Add(progressTitle);
        progressHint = Text("", 12, Muted); progressHint.Margin = new Thickness(0, 6, 0, 8); info.Children.Add(progressHint);
        status = Text("", 11, Good); info.Children.Add(status); Grid.SetColumn(info, 1); layout.Children.Add(info);
        var speed = Stack(); speed.VerticalAlignment = VerticalAlignment.Center; speed.HorizontalAlignment = HorizontalAlignment.Right;
        speed.Children.Add(Text(T("HOLD TARGET", "롱클릭 기준"), 9, Muted)); pending = Text(draft.LongPressMs + " ms", 23, Accent); pending.Margin = new Thickness(0, 4, 0, 0); speed.Children.Add(pending); Grid.SetColumn(speed, 2); layout.Children.Add(speed);
        return Card(layout, new Thickness(0, 0, 0, 18));
    }
    private void ControlsPage(StackPanel content)
    {
        var inputPanel = Stack(); inputPanel.Children.Add(Heading(T("Knob input", "노브 입력 방식")));
        var inputChoice = new ComboBox {
            ItemsSource = new[] {
                new ActionChoice { Key = KnobInput.Keyboard, Value = T("F13 keyboard input (default)", "F13 키보드 입력 (기본)") },
                new ActionChoice { Key = KnobInput.Receiver, Value = T("2.4 GHz receiver · Treble +", "2.4GHz 수신기 · Treble +") }
            }, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = draft.ClickInput,
            HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 300, FontSize = 12
        };
        AutomationProperties.SetName(inputChoice, T("Knob input mode", "노브 입력 방식"));
        inputChoice.SelectionChanged += delegate { if (building || inputChoice.SelectedValue == null) return; draft.ClickInput = (string)inputChoice.SelectedValue; Changed(); Build(); };
        inputPanel.Children.Add(inputChoice);
        var inputHint = Text(draft.ClickInput == KnobInput.Receiver
            ? T("In ANTICATER, map the center click to Media → Treble + and Save. Single, double, triple and hold use the receiver's press/release signals.", "ANTICATER에서 가운데 클릭을 Media → Treble +로 지정하고 Save하세요. 수신기의 누름·뗌 신호로 싱글·더블·트리플·롱클릭을 판정합니다.")
            : T("In ANTICATER, map the center click to Keyboard → F13 and Save. If F13 does not arrive over the supplied receiver, choose receiver mode above.", "ANTICATER에서 가운데 클릭을 Keyboard → F13으로 지정하고 Save하세요. 동봉 수신기에서 F13이 전달되지 않으면 위에서 수신기 방식을 선택하세요."), 11, Muted);
        inputHint.Margin = new Thickness(0, 12, 0, 0); inputPanel.Children.Add(inputHint);
        content.Children.Add(Card(inputPanel, new Thickness(0, 0, 0, 18)));
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var timing = Stack(); timing.Children.Add(Heading(T("Timing", "시간 기준")));
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        presets.Children.Add(Button(T("Fast", "빠르게"), () => Preset(220, 450), false)); presets.Children.Add(Button(T("Balanced", "균형"), () => Preset(320, 600), false)); presets.Children.Add(Button(T("Relaxed", "여유롭게"), () => Preset(450, 900), false)); timing.Children.Add(presets);
        timing.Children.Add(Timing(T("Next-click window", "다음 클릭 허용 간격"), draft.DoubleClickMs, 120, 1000, value => { draft.DoubleClickMs = draft.MultiClickMs = value; if (!draft.SeparateTripleGap) draft.TripleClickMs = value; Changed(); }));
        timing.Children.Add(Timing(T("Hold to activate", "롱클릭 자동 발동"), draft.LongPressMs, 150, 3000, value => { draft.LongPressMs = value; Changed(); }));
        var separate = new CheckBox { Content = T("Use a separate third-click window", "세 번째 클릭 간격을 따로 설정"), IsChecked = draft.SeparateTripleGap, Margin = new Thickness(0, 3, 0, 16), FontSize = 11 };
        separate.Click += delegate { draft.SeparateTripleGap = separate.IsChecked == true; if (!draft.SeparateTripleGap) draft.TripleClickMs = draft.DoubleClickMs; Changed(); Build(); }; timing.Children.Add(separate);
        if (draft.SeparateTripleGap) timing.Children.Add(Timing(T("Third-click window", "세 번째 클릭 허용 간격"), draft.TripleClickMs, 120, 1000, value => { draft.TripleClickMs = value; Changed(); }));
        timing.Children.Add(Text(T("Single and double wait for another click. Hold fires once while pressed.", "한 번·두 번은 다음 클릭을 기다립니다. 롱클릭은 누르는 중 한 번만 실행합니다."), 11, Muted));
        var timingCard = Card(timing, new Thickness(0, 0, 9, 0)); columns.Children.Add(timingCard);
        var actions = Stack(); actions.Children.Add(Heading(T("Click actions", "클릭별 동작")));
        ActionRow(actions, ClickGesture.Single, "01", draft.SingleAction, value => draft.SingleAction = value);
        ActionRow(actions, ClickGesture.Double, "02", draft.DoubleAction, value => draft.DoubleAction = value);
        ActionRow(actions, ClickGesture.Triple, "03", draft.TripleAction, value => draft.TripleAction = value);
        ActionRow(actions, ClickGesture.Long, "—", draft.LongAction, value => draft.LongAction = value);
        var hint = Text(T("Match the ANTICATER click mapping to the input mode above. Choose devices in Audio outputs.", "ANTICATER 클릭 매핑을 위 입력 방식에 맞추세요. 전환할 장치는 ‘출력 장치’에서 선택합니다."), 11, Muted); hint.Margin = new Thickness(0, 12, 0, 0); actions.Children.Add(hint);
        var actionsCard = Card(actions, new Thickness(9, 0, 0, 0)); Grid.SetColumn(actionsCard, 1); columns.Children.Add(actionsCard); content.Children.Add(columns);
    }
    private UIElement Timing(string caption, int value, int min, int max, Action<int> change)
    {
        var panel = Stack(); panel.Margin = new Thickness(0, 0, 0, 18); var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        row.Children.Add(Text(caption, 12, Ink)); var number = new TextBox { Text = value.ToString(), HorizontalContentAlignment = HorizontalAlignment.Right, FontSize = 14 }; AutomationProperties.SetName(number, caption); Grid.SetColumn(number, 1); row.Children.Add(number); var unit = Text("ms", 11, Muted); unit.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(unit, 2); row.Children.Add(unit); panel.Children.Add(row);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 10, IsSnapToTickEnabled = true, Margin = new Thickness(0, 8, 0, 0) }; AutomationProperties.SetName(slider, caption); panel.Children.Add(slider);
        bool syncing = false;
        slider.ValueChanged += delegate { if (building || syncing) return; syncing = true; number.Text = ((int)slider.Value).ToString(); number.BorderBrush = Brush("#32394E"); syncing = false; change((int)slider.Value); };
        number.TextChanged += delegate { if (building || syncing) return; int parsed; if (int.TryParse(number.Text, out parsed) && parsed >= min && parsed <= max) { syncing = true; slider.Value = parsed; syncing = false; number.BorderBrush = Brush("#32394E"); change(parsed); } else number.BorderBrush = Brush("#FFA591"); };
        number.LostKeyboardFocus += delegate { number.Text = ((int)slider.Value).ToString(); };
        return panel;
    }
    private void Preset(int gap, int hold) { draft.DoubleClickMs = draft.TripleClickMs = draft.MultiClickMs = gap; draft.SeparateTripleGap = false; draft.LongPressMs = hold; Changed(); Build(); }
    private void ActionRow(StackPanel parent, ClickGesture gesture, string badge, string value, Action<string> change)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var caption = new StackPanel { Orientation = Orientation.Horizontal }; var badgeText = Text(badge, 11, Accent); badgeText.Width = 26; caption.Children.Add(badgeText); caption.Children.Add(Text(Actions.GestureName(gesture), 12, Ink)); row.Children.Add(caption);
        var combo = new ComboBox { ItemsSource = Actions.Names.Keys.Select(key => new ActionChoice { Key = key, Value = Actions.Label(key) }).ToList(), DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = value, FontSize = 12 };
        AutomationProperties.SetName(combo, Actions.GestureName(gesture) + T(" action", " 동작")); combo.SelectionChanged += delegate { if (!building && combo.SelectedValue != null) { change((string)combo.SelectedValue); Changed(); } }; Grid.SetColumn(combo, 1); row.Children.Add(combo); parent.Children.Add(row);
    }
    private void AudioPage(StackPanel content)
    {
        var intro = new Grid(); intro.ColumnDefinitions.Add(new ColumnDefinition()); intro.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        intro.Children.Add(Text(T("Connected playback devices are detected automatically.", "연결된 재생 장치를 자동으로 인식합니다."), 12, Muted)); var refresh = Button(T("Refresh", "새로 고침"), () => RefreshAudio(true), false); Grid.SetColumn(refresh, 1); intro.Children.Add(refresh); content.Children.Add(Card(intro, new Thickness(0, 0, 0, 18)));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var available = Stack(); available.Children.Add(Heading(T("Available outputs", "사용 가능한 출력"))); available.Children.Add(Text(T("Check the devices to include in your cycle.", "반복 전환에 포함할 장치를 체크하세요."), 11, Muted)); audioList = Stack(); audioList.Margin = new Thickness(0, 18, 0, 0); available.Children.Add(audioList); grid.Children.Add(Card(available, new Thickness(0, 0, 9, 0)));
        var order = Stack(); order.Children.Add(Heading(T("Your cycle", "전환 순서"))); cycleList = Stack(); order.Children.Add(cycleList);
        var comms = new CheckBox { Content = T("Also switch communications output", "통신용 기본 출력도 함께 전환"), IsChecked = draft.SwitchCommunications, Margin = new Thickness(0, 18, 0, 18), FontSize = 11 }; comms.Click += delegate { draft.SwitchCommunications = comms.IsChecked == true; Changed(); }; order.Children.Add(comms);
        order.Children.Add(Button(T("Test cycle now", "지금 전환 테스트"), () => { string target = AudioDevices.Cycle(draft); Message(T("Output changed: ", "출력 변경: ") + target, false); RefreshAudio(true); }, true));
        var note = Text(T("This button changes the real Windows output, even in practice mode. Disconnected devices stay in your order and are skipped.", "이 버튼은 연습 모드에서도 실제 Windows 출력을 바꿉니다. 연결이 끊긴 장치는 순서를 유지하고 건너뜁니다."), 11, Muted); note.Margin = new Thickness(0, 16, 0, 0); order.Children.Add(note);
        var orderCard = Card(order, new Thickness(9, 0, 0, 0)); Grid.SetColumn(orderCard, 1); grid.Children.Add(orderCard); content.Children.Add(grid); RefreshAudio(true);
    }
    private void RefreshAudio(bool force)
    {
        if (audioList == null) return;
        try
        {
            devices = AudioDevices.List();
            string signature = string.Join("|", devices.Select(x => x.Id + x.Name + x.IsDefault)) + string.Join("|", draft.OutputDeviceIds);
            if (!force && signature == audioSignature) return; audioSignature = signature; audioList.Children.Clear(); cycleList.Children.Clear();
            if (devices.Count == 0) audioList.Children.Add(Text(T("No active playback devices. Connect a speaker, DAC or headset.", "연결된 재생 장치가 없습니다. 스피커·DAC·헤드셋을 연결하세요."), 12, Muted));
            foreach (var device in devices)
            {
                var local = device; var details = Stack(); details.Children.Add(Text(device.Name, 12, Ink));
                if (device.IsDefault) { var current = Text(T("CURRENT OUTPUT", "현재 기본 출력"), 9, Good); current.Margin = new Thickness(0, 5, 0, 0); details.Children.Add(current); }
                var check = new CheckBox { Content = details, IsChecked = draft.OutputDeviceIds.Contains(device.Id), Margin = new Thickness(0, 0, 0, 20) };
                AutomationProperties.SetName(check, device.Name); check.Click += delegate { if (check.IsChecked == true) { if (!draft.OutputDeviceIds.Contains(local.Id)) draft.OutputDeviceIds.Add(local.Id); } else draft.OutputDeviceIds.Remove(local.Id); Changed(); RefreshAudio(true); }; audioList.Children.Add(check);
            }
            if (draft.OutputDeviceIds.Count < 2) cycleList.Children.Add(Text(T("Select at least 2 outputs to enable cycling.", "장치를 두 개 이상 선택하면 전환할 수 있습니다."), 12, Accent));
            for (int i = 0; i < draft.OutputDeviceIds.Count; i++)
            {
                int index = i; string id = draft.OutputDeviceIds[i]; var device = devices.FirstOrDefault(x => x.Id == id);
                var row = new Grid { Margin = new Thickness(0, 8, 0, 8) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.Children.Add(Text((i + 1).ToString("00"), 11, Accent)); var name = Text(device == null ? T("Disconnected device", "연결되지 않은 장치") : device.Name, 11, device == null ? Muted : Ink); name.ToolTip = id; Grid.SetColumn(name, 1); row.Children.Add(name);
                var up = Button("↑", () => MoveDevice(index, -1), false); up.Padding = new Thickness(5); up.IsEnabled = i > 0; Grid.SetColumn(up, 2); row.Children.Add(up);
                var down = Button("↓", () => MoveDevice(index, 1), false); down.Padding = new Thickness(5); down.IsEnabled = i < draft.OutputDeviceIds.Count - 1; Grid.SetColumn(down, 3); row.Children.Add(down); cycleList.Children.Add(row);
                if (device == null) { var remove = Button(T("Remove disconnected device", "연결되지 않은 장치 제외"), () => { draft.OutputDeviceIds.Remove(id); Changed(); RefreshAudio(true); }, false); remove.FontSize = 10; remove.Margin = new Thickness(27, 0, 0, 10); cycleList.Children.Add(remove); }
            }
        }
        catch (Exception ex) { Message(ex.Message, true); }
    }
    private void MoveDevice(int index, int direction) { string id = draft.OutputDeviceIds[index]; draft.OutputDeviceIds.RemoveAt(index); draft.OutputDeviceIds.Insert(index + direction, id); Changed(); RefreshAudio(true); }
    private void CalibrationPage(StackPanel content)
    {
        var tools = Stack(); tools.Children.Add(Heading(T("Measure with intent", "의도를 지정해서 측정")));
        tools.Children.Add(Text(T("Choose a gesture, then perform it on your VK01 within 12 seconds. Actions are suppressed while measuring.", "동작 버튼을 고른 뒤 12초 안에 실제 VK01을 누르세요. 측정 중에는 실제 동작을 실행하지 않습니다."), 12, Muted));
        var trials = new UniformGrid { Columns = 4, Margin = new Thickness(0, 18, 0, 18) };
        foreach (ClickGesture gesture in Enum.GetValues(typeof(ClickGesture))) { ClickGesture local = gesture; trials.Children.Add(Button(Actions.GestureName(gesture), () => controller.StartTrial(local), false)); } tools.Children.Add(trials);
        history = new DataGrid { Height = 160, FontSize = 11, CanUserResizeRows = false, CanUserReorderColumns = false };
        string[] headers = { T("Intent / input", "의도 / 입력"), T("Detected", "실제 판정"), T("Held ms", "누름 ms"), T("Gap ms", "간격 ms"), T("Decision ms", "판정 ms"), T("Command", "명령") };
        string[] fields = { "Intent", "Detected", "Held", "Gap", "Decision", "Command" };
        for (int i = 0; i < fields.Length; i++) history.Columns.Add(new DataGridTextColumn { Header = headers[i], Binding = new Binding(fields[i]), Width = new DataGridLength(i == 0 ? 1.4 : 1, DataGridLengthUnitType.Star) });
        tools.Children.Add(history); rowsSignature = "";
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 14) };
        actionRow.Children.Add(Button(T("Suggest my timing", "측정값으로 추천"), () => { string explanation; draft = Calibration.Recommend(draft, records, out explanation); Changed(); Build(); Message(explanation, false); }, true));
        actionRow.Children.Add(Button(T("Clear history", "기록 초기화"), () => { records.Clear(); SaveRecords(); rowsSignature = ""; RefreshRows(); }, false)); tools.Children.Add(actionRow);
        tools.Children.Add(Text(T("Measure single, double and triple at least once each. Only labeled VK01 input trains suggestions; screen practice is excluded. Times start when Windows receives the first press, not when your finger touches the knob.", "한 번·두 번·세 번을 각각 한 차례 이상 측정하세요. 의도가 지정된 VK01 입력만 추천에 쓰며 화면 연습은 제외합니다. 시간은 Windows의 첫 누름 수신부터 측정하므로 기기 내부 지연은 포함되지 않습니다."), 11, Muted));
        content.Children.Add(Card(tools, new Thickness(0)));
    }
    private void Changed() { if (building) return; draft.Normalize(); controller.Preview(draft.Clone()); UpdateDirty(); }
    private void UpdateDirty()
    {
        bool dirty = new JavaScriptSerializer().Serialize(draft) != new JavaScriptSerializer().Serialize(controller.Saved);
        if (save != null) save.IsEnabled = dirty;
        if (savedNotice != null) savedNotice.Text = dirty ? (practice ? T("Unsaved changes · preview is active", "미저장 변경값 · 연습에 반영 중") : T("Unsaved changes · saved settings remain active", "미저장 변경값 · 저장된 설정으로 동작 중")) : T("All changes saved · closes to the tray", "저장됨 · 창을 닫아도 트레이에서 계속 동작");
    }
    private void UpdateLive()
    {
        var state = controller.State;
        if (connection != null) { connection.Text = state.Connected ? T("●  VK01 connected", "●  VK01 연결됨") : T("○  Connect your VK01", "○  VK01을 연결하세요"); connection.Foreground = Brush(state.Connected ? Good : Muted); }
        if (ring != null)
        {
            ring.State = state; ring.InvalidateVisual(); pending.Text = draft.LongPressMs + " ms";
            progressTitle.Text = state.Cancelled ? T("Gesture cancelled", "클릭 판정 취소") : state.LongFired ? T("Hold detected", "롱클릭 판정 완료") : state.Down ? T("Keep holding…", "누름 시간 측정 중…") : state.Pending > 0 ? T("Waiting for the next click", "다음 클릭을 기다립니다") : T("Try your VK01", "VK01으로 테스트하세요");
            progressHint.Text = state.Down ? (state.LongFired ? T("One gesture. No repeats on release.", "계속 누르거나 떼어도 한 번만 실행합니다.") : T("Activates in ", "발동까지 ") + Math.Max(0, state.LongMs - state.HoldMs) + " ms") : state.Pending > 0 ? state.WaitMs + T(" ms remaining", " ms 남음") : practice ? T("Or click here / press Space to practice.", "이 원을 클릭하거나 Space로 연습할 수도 있습니다.") : T("Live mode · turn on Practice to test without actions.", "실제 동작 중 · 실행 없이 테스트하려면 연습 모드를 켜세요.");
            status.Text = state.Status;
        }
        if (DateTime.UtcNow >= nextRefresh) { nextRefresh = DateTime.UtcNow.AddSeconds(2); if (page == "audio") RefreshAudio(false); }
        RefreshRows();
    }
    private void Message(string message, bool error) { if (feedback == null) return; feedback.Text = message; feedback.Foreground = Brush(error ? "#FFA591" : Good); }
    private void Recorded(Measurement item) { records.Add(item); if (records.Count > 80) records.RemoveAt(0); rowsSignature = ""; RefreshRows(); SaveRecords(); }
    private void RefreshRows()
    {
        if (history == null) return;
        string signature = new JavaScriptSerializer().Serialize(records) + Ui.Lang;
        if (signature == rowsSignature) return; rowsSignature = signature;
        var rows = records.Select(item => new MeasureRow {
            Intent = (item.Expected.HasValue ? Actions.GestureName(item.Expected.Value) : T("Free input", "자유 입력")) + " / " + (item.Source == "VK01" ? "VK01" : T("Screen", "화면")),
            Detected = item.DetectionText, Held = item.Holds.Count == 0 ? T("Holding", "누르는 중") : string.Join(", ", item.Holds), Gap = item.Gaps.Count == 0 ? "—" : string.Join(", ", item.Gaps),
            Decision = item.DecisionAt > 0 ? (item.DecisionAt - item.Start).ToString() : "—", Command = item.CommandMs >= 0 ? (item.Command.StartsWith("Error:") ? T("Error", "실패") : item.CommandMs + " ms") : T("Practice", "연습")
        }).ToList(); history.ItemsSource = rows; if (rows.Count > 0) history.ScrollIntoView(rows[rows.Count - 1]);
    }
    private void SaveRecords() { try { File.WriteAllText(historyPath, new JavaScriptSerializer().Serialize(records), new System.Text.UTF8Encoding(false)); } catch { } }
    private void LoadRecords() { try { if (File.Exists(historyPath)) records.AddRange(new JavaScriptSerializer().Deserialize<List<Measurement>>(File.ReadAllText(historyPath)).TakeLastCompat(80)); } catch { } }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class MeasureRow
{
    public string Intent { get; set; } public string Detected { get; set; } public string Held { get; set; }
    public string Gap { get; set; } public string Decision { get; set; } public string Command { get; set; }
}
internal sealed class ActionChoice
{
    public string Key { get; set; } public string Value { get; set; }
    public override string ToString() { return Value; }
}
internal static class EnumerableCompat
{
    internal static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> source, int count) { var list = source.ToList(); return list.Skip(Math.Max(0, list.Count - count)); }
}
internal sealed class ProgressRing : FrameworkElement
{
    internal ViewState State;
    protected override void OnRender(DrawingContext drawing)
    {
        var state = State; if (state == null) return;
        var center = new Point(50, 50); double radius = 43;
        var background = new SolidColorBrush(Color.FromRgb(50, 57, 78));
        var accent = new SolidColorBrush(state.LongFired ? Color.FromRgb(115, 226, 194) : Color.FromRgb(184, 164, 255));
        drawing.DrawEllipse(Brushes.Transparent, new Pen(background, 6), center, radius, radius);
        double progress = state.Down ? Math.Min(1, (double)state.HoldMs / state.LongMs) : state.Pending > 0 ? 1 - (double)state.WaitMs / (state.Pending == 1 ? state.DoubleMs : state.TripleMs) : 0;
        if (progress >= .999) drawing.DrawEllipse(null, new Pen(accent, 6), center, radius, radius);
        else if (progress > 0)
        {
            double angle = progress * Math.PI * 2; var end = new Point(50 + Math.Sin(angle) * radius, 50 - Math.Cos(angle) * radius);
            var path = new StreamGeometry(); using (var context = path.Open()) { context.BeginFigure(new Point(50, 7), false, false); context.ArcTo(end, new Size(radius, radius), 0, progress > .5, SweepDirection.Clockwise, true, false); }
            drawing.DrawGeometry(null, new Pen(accent, 6), path);
        }
        string value = state.Down ? state.HoldMs.ToString() : state.Pending > 0 ? state.Pending.ToString() : "VK01";
        var type = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var label = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, type, 22, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(label, new Point(50 - label.Width / 2, 25));
        var hint = new FormattedText(state.Down ? "ms" : state.Pending > 0 ? Ui.T("pending", "대기") : Ui.T("practice", "연습"), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, accent, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(hint, new Point(50 - hint.Width / 2, 56));
    }
}
