// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Noxel.App.Infrastructure;
using Noxel.App.Services;
using Noxel.Core.Display;
using Noxel.Core.Infrastructure;
using Noxel.Windows.DeviceApi;

namespace Noxel.App.Views;

/// <summary>
/// Monitor Control: one card per monitor, combining the desktop display (QueryDisplayConfig) with its monitor
/// device (SetupAPI). Display disconnect and device disable are separate actions with separate wording.
/// </summary>
public sealed partial class MonitorControlPage : Page
{
    private readonly NoxelController _c = App.Controller!;
    private IReadOnlyList<MonitorDevice> _devices = Array.Empty<MonitorDevice>();
    private bool _busy;

    public MonitorControlPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        Responsive.Attach(this);
        SmoothScroll.Attach(PageScroll);
        Loaded += (_, _) => { _c.MonitorsChanged += OnMonitorsChanged; _ = RescanAsync(); };
        Unloaded += (_, _) => _c.MonitorsChanged -= OnMonitorsChanged;
    }

    private void OnMonitorsChanged() { if (!_busy) _ = RescanAsync(refreshDisplays: false); }
    private void OnRefresh(object sender, RoutedEventArgs e) => _ = RescanAsync();

    /// <summary>Re-reads devices (off the UI thread) and, unless told otherwise, the display layout too.</summary>
    private async Task RescanAsync(bool refreshDisplays = true)
    {
        if (refreshDisplays) _c.RefreshMonitors();
        try { _devices = await Task.Run(MonitorDeviceService.Enumerate); }
        catch (Exception ex) { Log.Warn("Monitor device scan: " + ex.Message); _devices = Array.Empty<MonitorDevice>(); }
        Render();
    }

    private sealed record Row(MonitorInfo? Display, MonitorDevice? Device);

    private void Render()
    {
        var displays = _c.Monitors;
        var rows = new List<Row>();
        var matched = new HashSet<MonitorInfo>();
        foreach (var d in _devices)
        {
            var disp = MonitorControl.MatchDisplay(d, displays);
            if (disp is not null) matched.Add(disp);
            rows.Add(new Row(disp, d));
        }
        rows.AddRange(displays.Where(d => !matched.Contains(d)).Select(d => new Row(d, null)));
        rows = rows.OrderByDescending(r => r.Display?.IsActive ?? false).ThenByDescending(r => r.Display?.IsPrimary ?? false)
                   .ThenBy(r => r.Display?.FriendlyName ?? r.Device?.DeviceName).ToList();

        Rows.Children.Clear();
        foreach (var r in rows) Rows.Children.Add(BuildCard(r, displays));
        ScanNote.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScanNote.Text = "No monitors found. Check the cables, then choose Refresh.";
    }

    private UIElement BuildCard(Row r, IReadOnlyList<MonitorInfo> displays)
    {
        var res = Application.Current.Resources;
        var name = r.Display?.FriendlyName ?? DeviceNameOnly(r.Device!);
        var card = new Border { Style = (Style)res["GlassCard"] };
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = grid;

        var info = new StackPanel { Spacing = 8 };
        info.Children.Add(new TextBlock { Text = r.Display?.IsPrimary == true ? name + " (Primary)" : name, Style = (Style)res["CardTitle"], TextWrapping = TextWrapping.Wrap });

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (r.Display is { IsActive: true }) chips.Children.Add(Chip("On desktop", accent: true));
        else if (r.Display is not null) chips.Children.Add(Chip("Disconnected from desktop"));
        else chips.Children.Add(Chip("No picture detected"));
        if (r.Device is { } dev)
        {
            if (dev.IsDisabled) chips.Children.Add(Chip("Device disabled"));
            else if (dev.HasProblem) chips.Children.Add(Chip($"Device problem (code {dev.ProblemCode})"));
            else chips.Children.Add(Chip("Device enabled"));
            // No "Restart needed" chip: Windows raises that flag for monitor devices because the Generic Monitor driver
            // can't be unloaded live, but enabling/disabling takes effect at once (checked on the user's PC), so the chip
            // only caused doubt. The flag is still read (MonitorDevice.NeedsRestart) in case it is ever needed.
        }
        else chips.Children.Add(Chip("Device not identified"));
        info.Children.Add(chips);

        if (r.Display is { IsActive: true } a)
            info.Children.Add(new TextBlock { Text = $"{a.Current.Width}×{a.Current.Height} at {a.CurrentRefreshText} · {a.Connection}", Style = (Style)res["BodyText"] });
        else if (r.Display is { } inactive)
            info.Children.Add(new TextBlock { Text = $"Connected by {inactive.Connection}, not part of the desktop.", Style = (Style)res["BodyText"] });
        if (r.Device is { } d2)
            info.Children.Add(new TextBlock { Text = d2.InstanceId, Style = (Style)res["FaintText"], FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)res["NoxelMono"], IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(info);

        var actions = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, MinWidth = 200 };
        Grid.SetColumn(actions, 1);
        if (r.Display is { } disp)
        {
            if (disp.IsActive)
                actions.Children.Add(ActionButton("Disconnect display", MonitorControl.WhyNotDisconnect(disp, displays), () => _ = DisconnectAsync(disp)));
            else
                actions.Children.Add(ActionButton("Reconnect display", null, () => _ = ReconnectAsync(disp)));
        }
        if (r.Device is { } dv)
        {
            if (dv.IsDisabled)
                actions.Children.Add(ActionButton("Enable device", null, () => _ = ChangeDeviceAsync(dv, r.Display, DeviceAction.Enable), admin: true));
            else
                actions.Children.Add(ActionButton("Disable device", MonitorControl.WhyNotDisable(dv, r.Display, displays), () => _ = ChangeDeviceAsync(dv, r.Display, DeviceAction.Disable), admin: true));
        }
        grid.Children.Add(actions);
        // Narrow cards: buttons go below the details so the status chips keep the full width.
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 560;
            Grid.SetColumn(actions, narrow ? 0 : 1);
            Grid.SetRow(actions, narrow ? 1 : 0);
            Grid.SetColumnSpan(info, narrow ? 2 : 1);
            actions.MinWidth = narrow ? 0 : 200;
        };
        AutomationProperties.SetName(card, name);
        return card;
    }

    private static string DeviceNameOnly(MonitorDevice d) =>
        d.DeviceName.StartsWith("Generic Monitor (", StringComparison.OrdinalIgnoreCase) && d.DeviceName.EndsWith(')') ? d.DeviceName[17..^1] : d.DeviceName;

    private static Border Chip(string text, bool accent = false)
    {
        var res = Application.Current.Resources;
        return new Border
        {
            Style = (Style)res[accent ? "AccentBadge" : "Badge"],
            Child = new TextBlock { Text = text, Style = (Style)res[accent ? "AccentBadgeText" : "BadgeText"] },
        };
    }

    private FrameworkElement ActionButton(string text, string? blockedReason, Action run, bool admin = false)
    {
        var b = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = blockedReason is null && !_busy };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        if (admin) content.Children.Add(new FontIcon { Glyph = "", FontSize = 13 }); // shield: needs administrator approval
        content.Children.Add(new TextBlock { Text = text });
        b.Content = content;
        AutomationProperties.SetName(b, admin ? text + " (needs administrator approval)" : text);
        b.Click += (_, _) => run();
        if (blockedReason is null) { if (admin) ToolTipService.SetToolTip(b, "Windows will ask for administrator approval."); return b; }
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(b);
        panel.Children.Add(new TextBlock { Text = blockedReason, Style = (Style)Application.Current.Resources["FaintText"], MaxWidth = 260, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private ElementTheme Theme => ActualTheme;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        foreach (var b in Descendants<Button>(Rows)) b.IsEnabled = !busy && b.IsEnabled;
        if (!busy) Render();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }

    // ------------------------------------------------------------------ actions

    private async Task DisconnectAsync(MonitorInfo display)
    {
        if (_busy) return;
        _c.RefreshMonitors();
        var current = _c.Monitors.FirstOrDefault(m => m.TargetId == display.TargetId && m.TargetAdapter == display.TargetAdapter);
        if (current is null) { Notify("Monitor Control", "That display is no longer connected.", NoticeKind.Warning); Render(); return; }
        if (MonitorControl.WhyNotDisconnect(current, _c.Monitors) is { } why) { Notify("Can't disconnect", why, NoticeKind.Warning); Render(); return; }
        if (!await Dialogs.ConfirmDestructiveAsync(XamlRoot, Theme, $"Disconnect {current.FriendlyName}?",
                "It will be removed from your desktop and its windows move to your other display. NOXEL puts it back after "
                + $"{MonitorControlService.ConfirmSeconds} seconds unless you choose Keep.", "Disconnect"))
            return;

        SetBusy(true);
        try
        {
            var (result, pending) = MonitorControlService.Disconnect(current, _c.Monitors);
            if (pending is null) { Notify("Display not disconnected", result.Message, NoticeKind.Error); return; }
            _c.RefreshMonitors();
            bool keep = await CountdownAsync($"Keep {current.FriendlyName} disconnected?",
                "If you can't see this on a screen you expect, do nothing: it comes back on its own.", "Keep disconnected", "Put it back");
            var final = keep ? pending.Keep() : pending.Revert();
            Notify(final.Success ? (keep ? "Display disconnected" : "Display restored") : "Display layout", final.Success
                ? (keep ? $"{current.FriendlyName} is off the desktop. Use Reconnect to bring it back." : $"{current.FriendlyName} is back on the desktop.")
                : final.Message, final.Success ? NoticeKind.Success : NoticeKind.Error);
        }
        finally { _c.RefreshMonitors(); SetBusy(false); _ = RescanAsync(refreshDisplays: false); }
    }

    private async Task ReconnectAsync(MonitorInfo display)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var r = await Task.Run(() => MonitorControlService.Reconnect(display));
            Notify(r.Success ? "Display reconnected" : "Display not reconnected", r.Success ? $"{display.FriendlyName} is back on the desktop." : r.Message,
                r.Success ? NoticeKind.Success : NoticeKind.Error);
        }
        finally { _c.RefreshMonitors(); SetBusy(false); _ = RescanAsync(refreshDisplays: false); }
    }

    private async Task ChangeDeviceAsync(MonitorDevice device, MonitorInfo? display, DeviceAction action)
    {
        if (_busy) return;
        // Re-read everything: devices can come and go while the page is open.
        _c.RefreshMonitors();
        var fresh = await Task.Run(() => MonitorDeviceService.Find(device.InstanceId));
        if (fresh is null) { Notify("Monitor Control", "That monitor device is no longer connected.", NoticeKind.Warning); await RescanAsync(false); return; }
        var disp = MonitorControl.MatchDisplay(fresh, _c.Monitors);
        var name = disp?.FriendlyName ?? DeviceNameOnly(fresh);

        if (action == DeviceAction.Disable)
        {
            if (MonitorControl.WhyNotDisable(fresh, disp, _c.Monitors) is { } why) { Notify("Can't disable", why, NoticeKind.Warning); Render(); return; }
            if (!await Dialogs.ConfirmDestructiveAsync(XamlRoot, Theme, $"Disable the {name} monitor device?",
                    "This is the same as Disable device in Device Manager. Windows will ask for administrator approval. "
                    + $"NOXEL turns it back on after {MonitorControlService.ConfirmSeconds} seconds unless you choose Keep, "
                    + "and also if NOXEL closes before you answer.", "Disable device"))
                return;
        }
        else if (!fresh.IsDisabled) { Notify("Monitor Control", "That device is already enabled.", NoticeKind.Info); await RescanAsync(false); return; }

        SetBusy(true);
        try
        {
            // Gamma can't be undone on a display once its monitor device is off, so undo it first.
            if (action == DeviceAction.Disable) _c.PrepareMonitorDisable(disp);
            var (result, pending) = await MonitorControlService.ChangeDeviceAsync(fresh.InstanceId, action);
            if (!result.Success && action == DeviceAction.Disable) _c.MonitorDisableCancelled();
            if (!result.Success) { Notify(action == DeviceAction.Enable ? "Device not enabled" : "Device not disabled", result.Message, NoticeKind.Error); return; }
            if (pending is null)
            {
                Notify("Device enabled", $"{name} is enabled again.", NoticeKind.Success);
                return;
            }
            _c.RefreshMonitors();
            bool keep = await CountdownAsync($"Keep the {name} device disabled?",
                "If a screen went dark and you can't see this, do nothing: it turns back on by itself.", "Keep disabled", "Turn it back on");
            if (keep) pending.Keep(); else pending.Revert();
            var final = await pending.Final;
            Notify(final.Success ? (keep ? "Device disabled" : "Device enabled") : "Monitor device", final.Success
                ? (keep ? $"{name} is disabled. Enable it here or in Device Manager." : final.Message)
                : final.Message, final.Success ? NoticeKind.Success : NoticeKind.Error);
        }
        finally { _c.RefreshMonitors(); SetBusy(false); await RescanAsync(refreshDisplays: false); }
    }


    /// <summary>Asks to keep a change. Close (the safe choice) is focused; no answer within the countdown means undo.</summary>
    private async Task<bool> CountdownAsync(string title, string hint, string keepText, string undoText)
    {
        int left = MonitorControlService.ConfirmSeconds;
        var count = new TextBlock { Style = (Style)Application.Current.Resources["BodyText"] };
        var panel = new StackPanel { Spacing = 8, MaxWidth = 420 };
        panel.Children.Add(count);
        panel.Children.Add(new TextBlock { Text = hint, Style = (Style)Application.Current.Resources["MutedText"], TextWrapping = TextWrapping.Wrap });
        void Show() => count.Text = left == 1 ? "Undoing in 1 second." : $"Undoing in {left} seconds.";
        Show();
        var d = Dialogs.Create(XamlRoot, Theme, title, panel, keepText, undoText);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => { left--; if (left <= 0) { timer.Stop(); d.Hide(); } else Show(); };
        timer.Start();
        var answer = await d.ShowSafeAsync();
        timer.Stop();
        return answer == ContentDialogResult.Primary && left > 0;
    }

    private void Notify(string title, string text, NoticeKind kind) => _c.Raise(title, text, kind);
}
