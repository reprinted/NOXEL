using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Noxel.App.Infrastructure;
using Noxel.App.Services;
using Noxel.App.Views;
using Noxel.Core.Infrastructure;

namespace Noxel.App;

public sealed partial class MainWindow : Window
{
    private readonly bool _intro;
    private bool _ready, _splashDone;
    /// <summary>Set when NOXEL releases the window to the tray (close without exiting).</summary>
    public bool ReleaseToTray { get; set; }
    private string _pendingPage = "Dashboard";
    private NoxelController? C => App.Controller;

    public MainWindow(bool playIntro)
    {
        InitializeComponent();
        _intro = playIntro;
        Title = "NOXEL";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        WindowHelper.SetIcon(this);
        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

        var settings = App.Settings;
        WindowHelper.SetMinSize(this, 720, 540);
        WindowHelper.RestorePlacement(this, settings.MainPlacement, 1220, 800);
        if (settings.MainPlacement.Maximized && AppWindow.Presenter is OverlappedPresenter op) op.Maximize();
        ApplyTheme();

        AppWindow.Closing += OnClosing;
        AppWindow.Changed += OnWindowChanged;
        ThemeService.SystemThemeChanged += () => DispatcherQueue.TryEnqueue(ApplyTheme);

        if (_intro)
        {
            Shell.Opacity = 0;
            Splash.Visibility = Visibility.Visible;
            Splash.Finished += OnSplashFinished;
        }
        else _splashDone = true;
    }

    public void ApplyTheme() => ThemeService.ApplyToWindow(this, Root, App.Settings);

    // ------------------------------------------------------------ lifecycle

    public void OnBackendReady()
    {
        _ready = true;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        var c = C!;
        c.NoticeRaised += ShowNotice;
        c.ColorChanged += UpdateStatus;
        c.MonitorsChanged += UpdateStatus;
        c.PresetsChanged += UpdateStatus;
        Closed += (_, _) =>
        {
            c.NoticeRaised -= ShowNotice;
            c.ColorChanged -= UpdateStatus;
            c.MonitorsChanged -= UpdateStatus;
            c.PresetsChanged -= UpdateStatus;
        };
        UpdateStatus();
        NavigateTo(_pendingPage);
        if (_intro && !_splashDone) Splash.BackendReady();
    }

    private void OnSplashFinished(object? sender, EventArgs e)
    {
        _splashDone = true;
        Root.Children.Remove(Splash); // release all intro resources
        {
            var sb = new Storyboard();
            var a = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, Shell);
            Storyboard.SetTargetProperty(a, "Opacity");
            sb.Children.Add(a);
            sb.Begin();
        }
    }

    public void ShowFatal(Exception ex)
    {
        Splash.Visibility = Visibility.Collapsed;
        Shell.Opacity = 1;
        FatalPanel.Visibility = Visibility.Visible;
        FatalText.Text = ex.Message;
        FatalPath.Text = "Details were written to " + Path.Combine(AppPaths.Logs, "crash.log");
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) => App.OpenFolder(AppPaths.Logs);
    private void OnFatalExit(object sender, RoutedEventArgs e) => App.Current.ExitApp();

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        WindowHelper.SavePlacement(this, App.Settings.MainPlacement);
        if (App.IsExiting || ReleaseToTray) return;
        if (App.Settings.CloseToTray && _ready)
        {
            // Let the close happen: the window is released and NOXEL keeps running in the tray.
            App.Current.OnMainReleased(this);
            if (!App.Settings.TrayHintShown)
            {
                App.Settings.TrayHintShown = true;
                C?.SaveSoon();
                C?.Host.ShowBalloon("NOXEL is still running", "Your look and hotkeys stay active. Right-click the tray icon to exit.");
            }
            return;
        }
        args.Cancel = true;
        App.Current.ExitApp();
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange)
        {
            if (App.Settings.MinimizeToTray && _ready && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
                App.Current.HideMain();
        }
    }

    // ------------------------------------------------------------ navigation

    public void NavigateTo(string tag)
    {
        _pendingPage = tag;
        if (!_ready) return;
        Type page = tag switch
        {
            "Display" => typeof(DisplayPage),
            "Color" => typeof(ColorStudioPage),
            "Presets" => typeof(PresetsPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
        {
            NavigationTransitionInfo t = ThemeService.AnimationsAllowed(App.Settings)
                ? new EntranceNavigationTransitionInfo() : new SuppressNavigationTransitionInfo();
            ContentFrame.Navigate(page, null, t);
        }
        var item = tag == "Settings" ? Nav.SettingsItem : Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (item is not null && !ReferenceEquals(Nav.SelectedItem, item)) Nav.SelectedItem = item;
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) NavigateTo("Settings");
        else if (args.SelectedItem is NavigationViewItem { Tag: string tag }) NavigateTo(tag);
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "Compact" }) App.Current.ShowCompact();
    }

    // ------------------------------------------------------------ status & notices

    private void UpdateStatus()
    {
        var c = C;
        if (c is null) return;
        bool on = c.ColorEnabled;
        StatusDot.Opacity = on ? 1 : 0.3;
        var m = c.SelectedMonitor;
        StatusText.Text = $"{(on ? "Look on" : "Look off")} · {c.ActivePresetLabel}" + (m is null ? "" : $" · {m.Current.Width}×{m.Current.Height} @ {m.CurrentRefreshText}");
    }

    private void ShowNotice(Notice n)
    {
        var bar = new InfoBar
        {
            Title = n.Title,
            Message = n.Message,
            IsOpen = true,
            Severity = n.Kind switch
            {
                NoticeKind.Success => InfoBarSeverity.Success,
                NoticeKind.Warning => InfoBarSeverity.Warning,
                NoticeKind.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            },
        };
        bar.Closed += (s, _) => NoticeHost.Children.Remove((UIElement)s);
        NoticeHost.Children.Add(bar);
        while (NoticeHost.Children.Count > 3) NoticeHost.Children.RemoveAt(0);
        if (n.Kind is NoticeKind.Success or NoticeKind.Info)
        {
            var t = DispatcherQueue.CreateTimer();
            t.Interval = TimeSpan.FromSeconds(n.Kind == NoticeKind.Success ? 3.5 : 6);
            t.IsRepeating = false;
            t.Tick += (_, _) => { bar.IsOpen = false; NoticeHost.Children.Remove(bar); };
            t.Start();
        }
    }
}
