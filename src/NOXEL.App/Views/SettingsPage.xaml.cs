using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Noxel.App.Controls;
using Noxel.App.Infrastructure;
using Noxel.App.Services;
using Noxel.Core.Hotkeys;
using Noxel.Core.Infrastructure;
using Noxel.Core.Settings;
using Noxel.Windows.SystemIntegration;

namespace Noxel.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly NoxelController _c = App.Controller!;
    private AppSettings S => App.Settings;
    private bool _updating = true;
    private readonly Dictionary<HotkeyAction, (HotkeyRecorder Box, ToggleSwitch Enabled, TextBlock Error)> _hk = new();

    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        Responsive.Attach(this);
        SmoothScroll.Attach(PageScroll);
        BuildSwatches();
        BuildHotkeyRows();
        Loaded += (_, _) =>
        {
            _c.Updates.Changed += RefreshUpdates;
            _c.Hotkeys.RegistrationChanged += RefreshHotkeyErrors;
            _c.MonitorsChanged += RefreshDisplay;
            Load();
        };
        Unloaded += (_, _) =>
        {
            _c.Updates.Changed -= RefreshUpdates;
            _c.Hotkeys.RegistrationChanged -= RefreshHotkeyErrors;
            _c.MonitorsChanged -= RefreshDisplay;
        };
    }

    private void Load()
    {
        _updating = true;
        try
        {
            ThemeRadios.SelectedIndex = S.Theme switch { ThemePreference.Light => 1, ThemePreference.System => 2, _ => 0 };
            HexBox.Text = S.AccentHex;
            TransparencySwitch.IsOn = S.TransparencyEnabled;
            GlassSlider.Value = S.GlassIntensity;
            AnimationsSwitch.IsOn = S.AnimationsEnabled;
            MotionBlurSwitch.IsOn = S.MotionBlur;
            SmoothScrollSwitch.IsOn = S.SmoothScrolling;
            RespectSystemSwitch.IsOn = S.RespectSystemReducedMotion;
            ReducedMotionSwitch.IsOn = S.ReducedMotion;
            IntroSwitch.IsOn = S.PlayIntro;
            UpdateMotionDependencies();

            StartWithWindowsSwitch.IsOn = StartupRegistration.IsEnabled();
            StartupCompactSwitch.IsOn = S.StartupOpensCompact;
            ReapplySwitch.IsOn = S.ReapplyLookOnStartup;
            MinimizeTraySwitch.IsOn = S.MinimizeToTray;
            CloseTraySwitch.IsOn = S.CloseToTray;
            var presets = _c.Presets.All.ToList();
            DefaultPresetBox.ItemsSource = new[] { "None (keep the last look)" }.Concat(presets.Select(p => p.Name)).ToList();
            DefaultPresetBox.Tag = presets;
            DefaultPresetBox.SelectedIndex = S.DefaultPresetId is { } d ? 1 + presets.FindIndex(p => p.Id == d) : 0;
            if (DefaultPresetBox.SelectedIndex < 0) DefaultPresetBox.SelectedIndex = 0;

            HotkeysSwitch.IsOn = S.HotkeysEnabled;
            foreach (var (action, row) in _hk)
            {
                var b = S.Hotkeys[action];
                row.Box.Gesture = b.Gesture;
                row.Enabled.IsOn = b.Enabled;
                row.Box.IsEnabled = S.HotkeysEnabled;
                row.Enabled.IsEnabled = S.HotkeysEnabled;
            }
            RefreshHotkeyErrors();
            RefreshDisplay();
            ConfirmSlider.Value = S.ConfirmSeconds;
            LoggingSwitch.IsOn = S.DiagnosticLogging;
            UpdateModeBox.SelectedIndex = (int)S.UpdateMode;
            RefreshUpdates();

            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            AboutVersion.Text = $"Version {asm.GetName().Version?.ToString(3)} · build {info}";
            var company = asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            AboutDeveloper.Text = "Developer: " + (string.IsNullOrWhiteSpace(company) ? "not specified" : company);
        }
        finally { _updating = false; }
    }

    private void Save()
    {
        _c.SaveSoon();
    }

    // ------------------------------------------------------------ appearance

    private void BuildSwatches()
    {
        foreach (var (name, hex) in ThemeService.AccentPresets)
        {
            var color = ThemeService.ParseHex(hex)!.Value;
            var b = new Button
            {
                Width = 34, Height = 34, Padding = new Thickness(0), CornerRadius = new CornerRadius(17),
                Background = new SolidColorBrush(color), BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Tag = hex,
            };
            AutomationProperties.SetName(b, name + " accent");
            ToolTipService.SetToolTip(b, name);
            b.Click += (s, _) => SetAccent((string)((Button)s).Tag);
            Swatches.Children.Add(b);
        }
    }

    private void SetAccent(string hex)
    {
        var c = ThemeService.ParseHex(hex);
        if (c is null) { HexError.Text = "Use the #RRGGBB format, for example #9B7BFF."; return; }
        HexError.Text = "";
        S.AccentHex = ThemeService.ToHex(c.Value);
        HexBox.Text = S.AccentHex;
        ThemeService.ApplyAccent(c.Value);
        Save();
    }

    private void OnHexApply(object sender, RoutedEventArgs e) => SetAccent(HexBox.Text.Trim().StartsWith('#') ? HexBox.Text.Trim() : "#" + HexBox.Text.Trim());

    private void OnHexKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter) { OnHexApply(sender, e); e.Handled = true; }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || ThemeRadios.SelectedIndex < 0) return;
        var theme = ThemeRadios.SelectedIndex switch { 1 => ThemePreference.Light, 2 => ThemePreference.System, _ => ThemePreference.Dark };
        if (theme == S.Theme) return; // RadioButtons can report the old item while selection moves
        S.Theme = theme;
        App.Current.ApplyThemeToAllWindows();
        Save();
        _c.Raise("Theme changed", S.Theme switch { ThemePreference.Light => "Light", ThemePreference.System => "Following Windows", _ => "Dark" }, NoticeKind.Success);
    }

    private void OnAppearanceToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        S.TransparencyEnabled = TransparencySwitch.IsOn;
        S.AnimationsEnabled = AnimationsSwitch.IsOn;
        S.MotionBlur = MotionBlurSwitch.IsOn;
        S.SmoothScrolling = SmoothScrollSwitch.IsOn;
        S.RespectSystemReducedMotion = RespectSystemSwitch.IsOn;
        S.ReducedMotion = ReducedMotionSwitch.IsOn;
        ThemeService.ApplyGlass(S.TransparencyEnabled, S.GlassIntensity);
        App.Current.ApplyThemeToAllWindows();
        UpdateMotionDependencies();
        SmoothScroll.ApplySettings();
        Save();
    }

    /// <summary>
    /// Keeps dependent options honest: a saved preference is never erased, but a switch that currently has no
    /// effect is disabled with the reason shown, instead of pretending to work.
    /// </summary>
    private void UpdateMotionDependencies()
    {
        bool anim = ThemeService.AnimationsAllowed(S);
        bool systemOff = ThemeService.SystemReducedMotion;
        GlassSlider.IsEnabled = S.TransparencyEnabled;
        AnimationsSwitch.IsEnabled = !S.ReducedMotion;
        MotionBlurSwitch.IsEnabled = anim;
        MotionBlurNote.Text = anim
            ? "A short fading motion trail behind pages as they slide in, drawn by the Windows compositor. Never used while scrolling or on dialogs."
            : "Needs animations. It turns back on by itself when animations are allowed again.";
        bool smoothOk = ThemeService.SmoothScrollingAllowed(S);
        SmoothScrollSwitch.IsEnabled = smoothOk;
        SmoothScrollNote.Text = smoothOk
            ? "Use fluid scrolling and gentle momentum throughout NOXEL."
            : "Unavailable while reduced motion applies; scrolling stays standard. Touchpad and keyboard scrolling always work.";
        IntroSwitch.IsEnabled = !S.ReducedMotion;
        IntroStatus.Text = ThemeService.IntroStatus(S);
        SystemMotionNote.Text = systemOff
            ? (S.RespectSystemReducedMotion
                ? "On: Windows \"Animation effects\" is off on this PC, so NOXEL plays no intro, animations or smooth scrolling."
                : "Off (default): NOXEL uses its own settings above. Windows \"Animation effects\" is off on this PC; turn this on to follow it.")
            : "Windows \"Animation effects\" is on, so this switch makes no difference right now.";
    }

    private void OnGlassChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        S.GlassIntensity = e.NewValue;
        ThemeService.ApplyGlass(S.TransparencyEnabled, S.GlassIntensity);
        Save();
    }

    // ------------------------------------------------------------ startup

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (ReferenceEquals(sender, StartWithWindowsSwitch))
        {
            var err = StartupRegistration.SetEnabled(StartWithWindowsSwitch.IsOn);
            if (err is not null)
            {
                _c.Raise("Start with Windows", err, NoticeKind.Error);
                _updating = true; StartWithWindowsSwitch.IsOn = StartupRegistration.IsEnabled(); _updating = false;
            }
            S.StartWithWindows = StartWithWindowsSwitch.IsOn;
        }
        S.StartupOpensCompact = StartupCompactSwitch.IsOn;
        S.ReapplyLookOnStartup = ReapplySwitch.IsOn;
        S.MinimizeToTray = MinimizeTraySwitch.IsOn;
        S.CloseToTray = CloseTraySwitch.IsOn;
        S.PlayIntro = IntroSwitch.IsOn;
        S.SkipIntroOnRestore = true; // reopening from the tray never replays the intro
        UpdateMotionDependencies();
        Save();
    }

    private void OnDefaultPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || DefaultPresetBox.Tag is not List<Noxel.Core.Presets.Preset> list) return;
        S.DefaultPresetId = DefaultPresetBox.SelectedIndex > 0 ? list[DefaultPresetBox.SelectedIndex - 1].Id : null;
        Save();
    }

    // ------------------------------------------------------------ hotkeys

    private void BuildHotkeyRows()
    {
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            var label = new TextBlock { Text = HotkeyDefaults.Describe(action), Style = (Style)Application.Current.Resources["BodyText"], VerticalAlignment = VerticalAlignment.Center };
            var box = new HotkeyRecorder { HorizontalAlignment = HorizontalAlignment.Stretch };
            var a = action;
            box.Validator = g => _c.Hotkeys.FindConflict(g, a);
            var sw = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(sw, "Enable " + HotkeyDefaults.Describe(action));
            AutomationProperties.SetName(box, HotkeyDefaults.Describe(action) + " shortcut");
            var err = new TextBlock { Style = (Style)Application.Current.Resources["FaintText"], Visibility = Visibility.Collapsed };
            Grid.SetColumn(box, 1); Grid.SetColumn(sw, 2);
            Grid.SetRow(err, 1); Grid.SetColumnSpan(err, 3);
            grid.Children.Add(label); grid.Children.Add(box); grid.Children.Add(sw); grid.Children.Add(err);
            box.GestureChanged += (_, g) =>
            {
                S.Hotkeys[a].Gesture = g;
                _c.SaveNow();
                _c.Hotkeys.RegisterAll();
                _c.UpdateTray();
            };
            box.Rejected += (_, msg) => { err.Text = msg; err.Visibility = Visibility.Visible; };
            sw.Toggled += (_, _) =>
            {
                if (_updating) return;
                S.Hotkeys[a].Enabled = sw.IsOn;
                _c.SaveNow();
                _c.Hotkeys.RegisterAll();
            };
            _hk[action] = (box, sw, err);
            HotkeyRows.Children.Add(grid);
        }
    }

    private void RefreshHotkeyErrors()
    {
        foreach (var (action, row) in _hk)
        {
            var e = _c.Hotkeys.Errors.GetValueOrDefault(action);
            if (e is null && HotkeyGesture.TryParse(S.Hotkeys[action].Gesture, out var g) && g.IsBareFunctionKey && S.Hotkeys[action].Enabled)
                e = $"{g} alone is reserved by NOXEL in every app while it runs.";
            row.Error.Text = e ?? "";
            row.Error.Visibility = e is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void OnHotkeysToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        S.HotkeysEnabled = HotkeysSwitch.IsOn;
        _c.SaveNow();
        _c.Hotkeys.RegisterAll();
        Load();
    }

    private void OnResetHotkeys(object sender, RoutedEventArgs e)
    {
        S.Hotkeys = HotkeyDefaults.Create();
        S.HotkeysEnabled = true;
        _c.SaveNow();
        _c.Hotkeys.RegisterAll();
        Load();
    }

    // ------------------------------------------------------------ display

    private void RefreshDisplay()
    {
        bool was = _updating;
        _updating = true;
        try
        {
            var mons = _c.Monitors.Where(m => m.IsActive).ToList();
            PreferredMonitorBox.ItemsSource = new[] { "Primary monitor" }.Concat(mons.Select(m => m.DisplayLabel)).ToList();
            PreferredMonitorBox.Tag = mons;
            int i = S.PreferredMonitorId is null ? 0 : 1 + mons.FindIndex(m => m.StableId == S.PreferredMonitorId);
            PreferredMonitorBox.SelectedIndex = Math.Max(0, i);
            var tm = _c.ToggleMonitor;
            var n = tm is null ? null : _c.NativeFor(tm);
            var st = tm is null ? null : _c.StretchedFor(tm);
            NativeStretchedSummary.Text = $"Native: {(n is { } nn ? nn.ToString() : "unknown")} · Stretched: {(st is { } ss ? ss.ToString() : "not set")}";
        }
        finally { _updating = was; }
    }

    private void OnPreferredMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || PreferredMonitorBox.Tag is not List<Noxel.Core.Display.MonitorInfo> mons) return;
        S.PreferredMonitorId = PreferredMonitorBox.SelectedIndex > 0 ? mons[PreferredMonitorBox.SelectedIndex - 1].StableId : null;
        Save();
        _c.UpdateTray();
        RefreshDisplay();
    }

    private void OnConfirmChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        S.ConfirmSeconds = (int)e.NewValue;
        Save();
    }

    private void OnForgetModes(object sender, RoutedEventArgs e)
    {
        S.KnownGoodModes.Clear();
        Save();
        _c.Raise("Display", "NOXEL will ask you to confirm every display mode again.", NoticeKind.Success);
    }

    private void OnOpenDisplay(object sender, RoutedEventArgs e) => App.Main?.NavigateTo("Display");
    private void OnOpenPresets(object sender, RoutedEventArgs e) => App.Main?.NavigateTo("Presets");

    private void OnRestoreBuiltIns(object sender, RoutedEventArgs e)
    {
        _c.Presets.RestoreBuiltIns();
        _c.Raise("Built-in presets restored", "Favorites and shortcuts of built-in presets were reset and the Custom slot was cleared.", NoticeKind.Success);
    }

    // ------------------------------------------------------------ updates

    private void RefreshUpdates()
    {
        var u = _c.Updates;
        UpdCurrent.Text = $"v{u.Current} · {(u.Kind == InstallKind.Installed ? "installed copy" : "portable NOXEL.exe")}";
        UpdLatest.Text = u.Latest is { } r ? $"v{r.Version}" + (r.PublishedAt is { } p ? $" · {p.LocalDateTime:d MMM yyyy}" : "")
            : S.LatestKnownVersion is { } k ? $"v{k}" : "Not checked yet";
        UpdChecked.Text = S.LastUpdateCheck is { } t ? t.LocalDateTime.ToString("g") : "Never";
        UpdStatus.Text = u.Checking ? "Checking GitHub…"
            : u.Stage is DownloadStage.Downloading or DownloadStage.Verifying or DownloadStage.Ready or DownloadStage.Failed ? u.StageMessage
            : u.LastResult?.Message ?? "";
        bool available = u.LastResult?.Status == Noxel.Core.Updates.UpdateCheckStatus.UpdateAvailable;
        UpdCheck.IsEnabled = !u.Checking && u.Stage != DownloadStage.Downloading;
        UpdNotes.IsEnabled = u.Latest is not null;
        UpdDownload.IsEnabled = available && u.Stage is DownloadStage.None or DownloadStage.Failed;
        UpdInstall.IsEnabled = u.Stage == DownloadStage.Ready;
        bool busy = u.Stage is DownloadStage.Downloading or DownloadStage.Verifying;
        UpdProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy)
        {
            UpdFile.Text = u.AssetName;
            double pct = u.Total > 0 ? u.Received * 100.0 / u.Total : 0;
            UpdBar.Value = pct;
            UpdBar.IsIndeterminate = u.Stage == DownloadStage.Verifying;
            UpdProgressText.Text = u.Stage == DownloadStage.Verifying ? "Verifying SHA-256…"
                : $"{u.Received / 1048576.0:0.0} MB of {u.Total / 1048576.0:0.0} MB · {pct:0}% · {u.BytesPerSecond / 1048576.0:0.0} MB/s";
            UpdCancel.IsEnabled = u.Stage == DownloadStage.Downloading;
        }
    }

    private void OnUpdateModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || UpdateModeBox.SelectedIndex < 0) return;
        S.UpdateMode = (UpdateMode)UpdateModeBox.SelectedIndex;
        Save();
    }

    private async void OnUpdateCheck(object sender, RoutedEventArgs e) => await _c.Updates.CheckAsync(manual: true);
    private async void OnUpdateNotes(object sender, RoutedEventArgs e) => await App.Current.ShowReleaseNotesAsync();
    private async void OnUpdateDownload(object sender, RoutedEventArgs e) => await _c.Updates.DownloadAsync();
    private void OnUpdateCancel(object sender, RoutedEventArgs e) => _c.Updates.CancelDownload();
    private async void OnUpdateInstall(object sender, RoutedEventArgs e) => await App.Current.ConfirmInstallUpdateAsync();

    // ------------------------------------------------------------ diagnostics

    private void OnLoggingToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        S.DiagnosticLogging = LoggingSwitch.IsOn;
        Log.Enabled = S.DiagnosticLogging;
        Save();
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) => App.OpenFolder(AppPaths.Logs);
    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) => App.OpenFolder(AppPaths.Root);
    private void OnOpenProgramFolder(object sender, RoutedEventArgs e) => App.OpenFolder(Path.GetDirectoryName(StartupRegistration.ExePath()) ?? AppContext.BaseDirectory);

    private void OnOpenLicense(object sender, RoutedEventArgs e) => OpenTextFile("LICENSE.txt");
    private void OnOpenNotices(object sender, RoutedEventArgs e) => OpenTextFile("THIRD-PARTY-NOTICES.txt");

    // The copy next to NOXEL.exe (installer / release folder) wins; the bundled copy is the fallback for a lone exe.
    private void OpenTextFile(string name)
    {
        var beside = Path.Combine(Path.GetDirectoryName(StartupRegistration.ExePath()) ?? "", name);
        var path = File.Exists(beside) ? beside : Path.Combine(AppContext.BaseDirectory, name);
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException(name);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {name}: {ex.Message}");
            _c.Raise(name, "The file could not be opened.", NoticeKind.Warning);
        }
    }

    private void OnDeleteLogs(object sender, RoutedEventArgs e)
    {
        Log.DeleteAll();
        _c.Raise("Diagnostics", "Logs deleted.", NoticeKind.Success);
    }
}
