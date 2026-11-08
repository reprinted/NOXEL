using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
        bool intro = !startupLaunch && (forceIntro || ThemeService.IntroAllowed(Settings));

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
