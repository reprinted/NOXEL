using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Noxel.App.Infrastructure;
using Noxel.App.Services;
using Noxel.App.Views;
using Noxel.Core.Infrastructure;
using Noxel.Core.Settings;
using Noxel.Windows.SystemIntegration;

namespace Noxel.App;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;
    public static NoxelController? Controller { get; private set; }
    public static AppSettings Settings => _settingsStore!.Current;
    public static bool IsExiting { get; private set; }
    public static bool IsMainWindowHidden => _main is null || !_main.AppWindow.IsVisible;
    public static MainWindow? Main => _main;

    private static SettingsStore? _settingsStore;
    private static MainWindow? _main;
    private CompactWindow? _compact;
    private ConfirmDisplayWindow? _confirm;
    private DispatcherQueue _ui = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Crash("UI thread", e.Exception);
            if (Controller?.IsReady == true)
            {
                // Keep running when a single view action fails; report it instead of crashing.
                e.Handled = true;
                Controller.Raise("Something went wrong", e.Exception.Message + " (details in the crash log)", NoticeKind.Error);
            }
            else EmergencyRestore();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ui = DispatcherQueue.GetForCurrentThread();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        _settingsStore = new SettingsStore(AppPaths.Settings);
        _settingsStore.Load();
        Log.Enabled = Settings.DiagnosticLogging;
        Log.Info("NOXEL starting");
        ThemeService.Initialize(Resources, Settings);

        bool startupLaunch = Program.Args.Any(a => a.Equals(StartupRegistration.StartupArg, StringComparison.OrdinalIgnoreCase));
        bool forceIntro = Program.Args.Any(a => a.Equals("--intro", StringComparison.OrdinalIgnoreCase));
        bool justUpdated = Program.Args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
        bool intro = !startupLaunch && !justUpdated && (forceIntro || ThemeService.IntroAllowed(Settings));

        Controller = new NoxelController(_ui, _settingsStore);
        Controller.WindowRequested += OnWindowRequested;
        Controller.ExitRequested += ExitApp;

        if (!startupLaunch)
        {
            _main = new MainWindow(intro);
            var page = Program.Args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
            if (page is not null) _main.NavigateTo(page["--page=".Length..]);
            _main.Activate();
        }

        // Opening NOXEL again (exe, shortcut, Start menu) while it runs in the tray is a launch: play the intro.
        Program.Instance?.ListenForActivation(() => _ui.TryEnqueue(() => ShowMain(page: null, relaunch: true)));

        try
        {
            await Controller.InitializeAsync(startupLaunch);
        }
        catch (Exception ex)
        {
            Log.Crash("Startup", ex);
            _main ??= new MainWindow(false);
            _main.Activate();
            _main.ShowFatal(ex);
            return;
        }

        Controller.Changes.PendingStarted += OnPendingDisplayChange;
        Controller.ReleaseNotesRequested += () => _ = ShowReleaseNotesAsync();
        Controller.InstallUpdateRequested += () => _ = ConfirmInstallUpdateAsync();
        if (justUpdated)
            _ui.TryEnqueue(() => Controller.RaiseNotice(new Notice($"Updated to NOXEL v{Services.UpdateService.CurrentVersion()}",
                "Your settings, presets and shortcuts were kept.", NoticeKind.Success)));
        _main?.OnBackendReady();
        if (startupLaunch && Settings.StartupOpensCompact) ShowCompact();
    }

    // ------------------------------------------------------------ windows

    private void OnWindowRequested(WindowRequest r)
    {
        switch (r)
        {
            case WindowRequest.Main: ShowMain(); break;
            case WindowRequest.Compact: ShowCompact(); break;
            case WindowRequest.Settings: ShowMain("Settings"); break;
            case WindowRequest.Toggle:
                if (IsMainWindowHidden) ShowMain(); else HideMain();
                break;
        }
    }

    public void ShowMain(string? page = null, bool relaunch = false)
    {
        if (IsExiting) return;
        if (_main is null)
        {
            // Tray icon / tray menu restore is instant; launching NOXEL again counts as opening it and plays the intro.
            bool intro = (relaunch || !Settings.SkipIntroOnRestore) && ThemeService.IntroAllowed(Settings);
            _main = new MainWindow(intro);
            if (Controller?.IsReady == true) _main.OnBackendReady();
        }
        WindowHelper.BringToFront(_main);
        if (page is not null) _main.NavigateTo(page);
    }

    /// <summary>
    /// Sends NOXEL to the tray. The main window is closed (not just hidden) so its XAML tree, composition
    /// surfaces and backdrop are released; the backend, hotkeys and color look keep running. Reopening
    /// rebuilds the window in a few hundred milliseconds, without the intro.
    /// </summary>
    public void HideMain()
    {
        if (_main is null) return;
        var w = _main;
        WindowHelper.SavePlacement(w, Settings.MainPlacement);
        w.ReleaseToTray = true;
        OnMainReleased(w);
        _ui.TryEnqueue(() => w.Close()); // outside any window event handler
    }

    /// <summary>The main window is going away but NOXEL stays in the tray.</summary>
    public void OnMainReleased(MainWindow w)
    {
        if (ReferenceEquals(_main, w)) _main = null;
        Controller?.SaveSoon();
        TrimMemorySoon();
    }

    private DispatcherQueueTimer? _trimTimer; // field: an unrooted timer can be collected before it fires

    /// <summary>
    /// After the window is released, compact the managed heap and log real memory use. NOXEL deliberately does not
    /// force-trim its working set: measurements showed private bytes stay ~116 MB either way (almost all of it is
    /// the GPU driver and WinUI compositor, not NOXEL's ~1 MB managed heap), so trimming would only make the number
    /// in Task Manager look smaller while causing page faults when the window comes back.
    /// </summary>
    private void TrimMemorySoon()
    {
        if (_trimTimer is null)
        {
            _trimTimer = _ui.CreateTimer();
            _trimTimer.Interval = TimeSpan.FromSeconds(2);
            _trimTimer.IsRepeating = false;
            _trimTimer.Tick += (_, _) =>
            {
                if (_main is not null || _compact is not null) return;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                using var self = Process.GetCurrentProcess();
                var gc = GC.GetGCMemoryInfo();
                Log.Info($"Tray: managed heap {GC.GetTotalMemory(false) / 1048576.0:0.0} MB, GC committed {gc.TotalCommittedBytes / 1048576.0:0.0} MB, private {self.PrivateMemorySize64 / 1048576.0:0.0} MB, working set {self.WorkingSet64 / 1048576.0:0.0} MB");
            };
        }
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    public void ShowCompact()
    {
        if (IsExiting || Controller?.IsReady != true) return;
        _compact ??= new CompactWindow();
        _compact.Closed += (_, _) => _compact = null;
        WindowHelper.BringToFront(_compact);
        if (_main is not null && _main.AppWindow.IsVisible) HideMain();
    }

    public void CloseCompactAndShowMain()
    {
        _compact?.Close();
        _compact = null;
        ShowMain();
    }

    public void ApplyThemeToAllWindows()
    {
        _main?.ApplyTheme();
        _compact?.ApplyTheme();
        _confirm?.ApplyTheme();
    }

    private void OnPendingDisplayChange(PendingDisplayChange p)
    {
        _confirm?.Close();
        _confirm = new ConfirmDisplayWindow(p);
        _confirm.Closed += (_, _) => _confirm = null;
        _confirm.ShowCentered();
    }

    // ------------------------------------------------------------ exit

    public void ExitApp()
    {
        if (IsExiting) return;
        IsExiting = true;
        Log.Info("NOXEL exiting");
        if (_main is not null) WindowHelper.SavePlacement(_main, Settings.MainPlacement);
        _compact?.SavePlacement();
        try { Controller?.Shutdown(); } catch (Exception ex) { Log.Crash("Shutdown", ex); }
        try { _confirm?.Close(); } catch { }
        try { _compact?.Close(); } catch { }
        try { _main?.Close(); } catch { }
        Exit();
    }

    // ------------------------------------------------------------ updates

    public async Task ShowReleaseNotesAsync()
    {
        ShowMain();
        var r = Controller?.Updates.Latest;
        if (_main?.Content?.XamlRoot is not { } root || r is null) return;
        var text = FormatNotes(string.IsNullOrWhiteSpace(r.Notes) ? "No release notes were published for this version." : r.Notes);
        var content = new StackPanel { Spacing = 10, MinWidth = 400, MaxWidth = 500 };
        content.Children.Add(new TextBlock
        {
            Text = $"Published {(r.PublishedAt is { } p ? p.LocalDateTime.ToString("d MMM yyyy") : "date unknown")} · installed v{Controller!.Updates.Current}",
            Style = (Style)Resources["FaintText"],
        });
        content.Children.Add(new ScrollViewer { Content = text, MaxHeight = 320 });
        var d = Dialogs.Create(root, _main.Content is FrameworkElement fe ? fe.ActualTheme : ElementTheme.Default,
            r.Title, content, "Open on GitHub", "Close");
        if (await d.ShowSafeAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && (r.PageUrl ?? new Uri(Noxel.Core.Updates.UpdateSource.ReleasesPage)) is { } url)
            await global::Windows.System.Launcher.LaunchUriAsync(url);
    }

    /// <summary>Light Markdown for GitHub release notes: headings, bullets, **bold**, `code` and [links](url) as text.</summary>
    private UIElement FormatNotes(string markdown)
    {
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(0, 0, 12, 0) };
        var body = (Style)Resources["BodyText"];
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0) continue;
            var heading = System.Text.RegularExpressions.Regex.Match(line, @"^\s*#{1,6}\s+(.*)$");
            var bullet = System.Text.RegularExpressions.Regex.Match(line, @"^(\s*)[-*+]\s+(.*)$");
            var tb = new TextBlock { Style = body, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
            if (heading.Success)
            {
                AddInlines(tb, heading.Groups[1].Value);
                tb.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                tb.Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 8, 0, 0);
                panel.Children.Add(tb);
            }
            else if (bullet.Success)
            {
                AddInlines(tb, bullet.Groups[2].Value);
                var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(Math.Min(bullet.Groups[1].Value.Length, 8) * 6, 0, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = "•", Style = body });
                Grid.SetColumn(tb, 1);
                row.Children.Add(tb);
                panel.Children.Add(row);
            }
            else { AddInlines(tb, line.Trim()); panel.Children.Add(tb); }
        }
        return panel;

        static void AddInlines(TextBlock tb, string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\[([^\]]+)\]\([^)]+\)", "$1").Replace("`", "");
            var parts = System.Text.RegularExpressions.Regex.Split(s.Replace("__", "**"), @"\*\*(.+?)\*\*"); // odd indexes = bold
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                var run = new Microsoft.UI.Xaml.Documents.Run { Text = parts[i] };
                if (i % 2 == 1) run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                tb.Inlines.Add(run);
            }
        }
    }

    public async Task ConfirmInstallUpdateAsync()
    {
        var u = Controller?.Updates;
        if (u is null) return;
        ShowMain();
        if (_main?.Content?.XamlRoot is not { } root) return;
        var blocker = u.InstallBlocker();
        if (blocker is not null) { Controller!.RaiseNotice(new Notice("Can't install yet", blocker, NoticeKind.Warning)); return; }
        var how = u.Kind == InstallKind.Installed
            ? "NOXEL will close, run the verified installer and reopen."
            : "NOXEL will close, replace this NOXEL.exe with the verified new version (the old one is kept as NOXEL.previous.exe) and reopen.";
        var d = Dialogs.Create(root, _main.Content is FrameworkElement fe ? fe.ActualTheme : ElementTheme.Default,
            $"Install NOXEL v{u.ReadyVersion}?", how + " Your color look is turned off first and your settings and presets are kept.",
            "Install and restart", "Not now", primaryIsDefault: true);
        if (await d.ShowSafeAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        var err = u.Install();
        if (err is not null) Controller!.RaiseNotice(new Notice("Update not installed", err, NoticeKind.Error));
    }

    public static void EmergencyRestore()
    {
        try { Controller?.EmergencyRestore(); } catch { }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("Open folder", ex); }
    }
}
