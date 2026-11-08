using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noxel.App.Services;
using Noxel.App.ViewModels;
using Noxel.Core.Display;
using Noxel.Core.Hotkeys;
using Noxel.Core.Settings;

namespace Noxel.App.Views;

public sealed partial class DisplayPage : Page
{
    private readonly NoxelController _c = App.Controller!;
    private List<MonitorInfo> _monitors = new();
    private MonitorInfo? _m;
    private List<ResolutionItem> _all = new();
    private ResolutionGroup? _selected;
    private int _wantedHz;
    private bool _updating, _applying;

    public DisplayPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { _c.MonitorsChanged += Refresh; Refresh(); };
        Unloaded += (_, _) => _c.MonitorsChanged -= Refresh;
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            _monitors = _c.Monitors.Where(m => m.IsActive).ToList();
            MonitorBox.ItemsSource = _monitors.Select(m => m.DisplayLabel).ToList();
            _m = _c.SelectedMonitor;
            MonitorBox.SelectedIndex = _m is null ? -1 : _monitors.FindIndex(x => x.StableId == _m.StableId);

            var inactive = _c.Monitors.Where(m => !m.IsActive).ToList();
            InactiveCard.Visibility = inactive.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            InactiveList.Text = string.Join("\n", inactive.Select(m => $"{m.FriendlyName} · {m.Connection}"));

            if (_m is null)
            {
                CurText.Text = CurHzText.Text = NativeText.Text = OrientText.Text = ConnText.Text = RoleText.Text = "—";
                IdText.Text = "No active monitor detected.";
                ResGrid.ItemsSource = null;
                return;
            }
            CurText.Text = $"{_m.Current.Width}×{_m.Current.Height}";
            CurHzText.Text = _m.CurrentRefreshText;
            NativeText.Text = _m.HasPreferred ? $"{_m.PreferredWidth}×{_m.PreferredHeight}" : "Unknown";
            OrientText.Text = _m.Orientation switch
            {
                DisplayOrientation.Portrait => "Portrait",
                DisplayOrientation.LandscapeFlipped => "Landscape (flipped)",
                DisplayOrientation.PortraitFlipped => "Portrait (flipped)",
                _ => "Landscape",
            };
            ConnText.Text = _m.Connection;
            RoleText.Text = _m.IsPrimary ? "Primary" : "Secondary";
            IdText.Text = $"{_m.StableId} · {_m.DeviceName} · {_m.AdapterName}";
            ScalingNote.Text = "Current scaling: " + ScalingName(_m.CurrentScaling) + ".";

            var favs = App.Settings.FavoriteResolutions.ToHashSet();
            _all = _m.Resolutions
                .Select(g => new ResolutionItem
                {
                    Group = g,
                    IsNative = _m.IsNative(g.Width, g.Height),
                    IsCurrent = g.Width == _m.Current.Width && g.Height == _m.Current.Height,
                    IsFavorite = favs.Contains(AppSettings.ResolutionKey(_m.StableId, g.Width, g.Height)),
                })
                .OrderByDescending(i => i.IsFavorite)
                .ToList();
            ApplyFilter();
            RefreshNativeStretched();
        }
        finally { _updating = false; }
        if (_selected is null && _m is not null) SelectResolution(_m.Current.Width, _m.Current.Height);
        else if (_selected is not null) SelectResolution(_selected.Width, _selected.Height);
    }

    private void ApplyFilter()
    {
        var q = (SearchBox.Text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("*", "x").Replace("×", "x");
        var aspect = AspectBox.SelectedItem as string ?? "All";
        var list = _all.Where(i =>
            (q.Length == 0 || $"{i.Width}x{i.Height}".Contains(q)) &&
            (aspect == "All" || AspectRatio.Family(i.Width, i.Height) == aspect)).ToList();
        var keep = _selected;
        bool was = _updating;
        _updating = true;
        ResGrid.ItemsSource = list;
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (keep is not null)
        {
            var match = list.FirstOrDefault(i => i.Width == keep.Width && i.Height == keep.Height);
            if (match is not null) ResGrid.SelectedItem = match;
        }
        _updating = was;
    }

    private void SelectResolution(int w, int h)
    {
        if (ResGrid.ItemsSource is not List<ResolutionItem> list) return;
        var item = list.FirstOrDefault(i => i.Width == w && i.Height == h);
        if (item is null) return;
        ResGrid.SelectedItem = item;
        ResGrid.ScrollIntoView(item);
        OnSelected(item.Group);
    }

    private void OnResolutionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (ResGrid.SelectedItem is ResolutionItem item) OnSelected(item.Group);
    }

    private void OnSelected(ResolutionGroup g)
    {
        if (_m is null) return;
        int previousHz = HzBox.SelectedItem is RefreshItem r ? r.Hz : _wantedHz;
        _selected = g;
        SelText.Text = $"{g.Width} × {g.Height}";
        bool isCurrentRes = g.Width == _m.Current.Width && g.Height == _m.Current.Height;
        var items = g.RefreshRates.Select((hz, i) => new RefreshItem
        {
            Hz = hz,
            Label = $"{hz} Hz" + (i == 0 ? "  · highest" : "") + (isCurrentRes && hz == _m.Current.RefreshHz ? "  · current" : ""),
        }).ToList();
        _updating = true;
        HzBox.ItemsSource = items;
        // Preserve a still-valid selection; otherwise fall back to the current rate, then the highest.
        int want = previousHz > 0 ? previousHz : isCurrentRes ? _m.Current.RefreshHz : g.MaxRefresh;
        var pick = items.FirstOrDefault(i => i.Hz == want);
        if (pick is null && previousHz > 0)
        {
            ModeWarning.Text = $"{previousHz} Hz isn't available at {g.Width}×{g.Height}. Pick one of the supported rates.";
            ModeWarning.Visibility = Visibility.Visible;
        }
        else ModeWarning.Visibility = Visibility.Collapsed;
        pick ??= items.FirstOrDefault(i => isCurrentRes && i.Hz == _m.Current.RefreshHz) ?? items.FirstOrDefault();
        HzBox.SelectedItem = pick;
        _updating = false;
        HzCurrent.Text = _m.CurrentRefreshText + (isCurrentRes ? "" : $" at {_m.Current.Width}×{_m.Current.Height}");
        HzMax.Text = $"{g.MaxRefresh} Hz";
        UpdateSelectedHz();
    }

    private void OnHzChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (HzBox.SelectedItem is RefreshItem r) _wantedHz = r.Hz;
        ModeWarning.Visibility = Visibility.Collapsed;
        UpdateSelectedHz();
    }

    private void UpdateSelectedHz()
    {
        var r = HzBox.SelectedItem as RefreshItem;
        HzSelected.Text = r is null ? "—" : $"{r.Hz} Hz";
        bool same = _m is not null && _selected is not null && r is not null && _m.Current == new DisplayMode(_selected.Width, _selected.Height, r.Hz);
        ApplyButton.IsEnabled = !_applying && r is not null && _selected is not null && (!same || ScalingBox.SelectedIndex > 0);
        ApplyButton.Content = same ? "Already active" : "Apply";
        if (ScalingBox.SelectedIndex > 0) ApplyButton.Content = "Apply";
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_m is null || _selected is null || HzBox.SelectedItem is not RefreshItem r) return;
        _applying = true;
        ApplyButton.IsEnabled = false;
        try
        {
            var scaling = ScalingBox.SelectedIndex switch { 1 => GpuScaling.DriverDefault, 2 => GpuScaling.Stretch, 3 => GpuScaling.Center, _ => GpuScaling.DontChange };
            await _c.ApplyModeAsync(_m, new DisplayMode(_selected.Width, _selected.Height, r.Hz), scaling, forceConfirm: true);
        }
        finally
        {
            _applying = false;
            UpdateSelectedHz();
        }
    }

    private void OnScalingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && ApplyButton is not null) UpdateSelectedHz();
    }

    private void OnFilterChanged(object sender, object e)
    {
        if (_updating || _m is null) return;
        ApplyFilter();
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || MonitorBox.SelectedIndex < 0) return;
        _selected = null;
        _wantedHz = 0;
        _c.SelectMonitor(_monitors[MonitorBox.SelectedIndex].StableId);
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => _c.RefreshMonitors();

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_m is null || (sender as FrameworkElement)?.Tag is not ResolutionItem item) return;
        var key = AppSettings.ResolutionKey(_m.StableId, item.Width, item.Height);
        var favs = App.Settings.FavoriteResolutions;
        if (!favs.Remove(key)) favs.Add(key);
        _c.SaveSoon();
        Refresh();
    }

    // ------------------------------------------------------------ native / stretched

    private void RefreshNativeStretched()
    {
        if (_m is null) return;
        var groups = _m.Resolutions.ToList();
        var s = App.Settings;
        NativeBox.ItemsSource = new[] { _m.HasPreferred ? $"Monitor native ({_m.PreferredWidth}×{_m.PreferredHeight})" : "Monitor native" }
            .Concat(groups.Select(g => $"{g.Text}  ·  {g.AspectLabel}")).ToList();
        NativeBox.SelectedIndex = s.NativeMode is null ? 0 : 1 + groups.FindIndex(g => g.Width == s.NativeMode.Width && g.Height == s.NativeMode.Height);
        if (NativeBox.SelectedIndex < 0) NativeBox.SelectedIndex = 0;

        var stretchedOrder = groups.OrderByDescending(g => AspectRatio.Family(g.Width, g.Height) is "4:3" or "5:4").ToList();
        StretchedBox.Tag = stretchedOrder;
        StretchedBox.ItemsSource = new[] { "Not set" }.Concat(stretchedOrder.Select(g => $"{g.Text}  ·  {g.AspectLabel}")).ToList();
        int si = s.StretchedMode is null ? 0 : 1 + stretchedOrder.FindIndex(g => g.Width == s.StretchedMode.Width && g.Height == s.StretchedMode.Height);
        StretchedBox.SelectedIndex = Math.Max(0, si);
        var sg = si > 0 ? stretchedOrder[si - 1] : null;
        StretchedHzBox.ItemsSource = new[] { "Refresh: highest available" }.Concat((sg?.RefreshRates ?? Array.Empty<int>()).Select(h => $"Refresh: {h} Hz")).ToList();
        StretchedHzBox.SelectedIndex = sg is null || s.StretchedMode is null || s.StretchedMode.RefreshHz == 0 ? 0 : 1 + sg.RefreshRates.ToList().IndexOf(s.StretchedMode.RefreshHz);
        if (StretchedHzBox.SelectedIndex < 0) StretchedHzBox.SelectedIndex = 0;
        StretchedHzBox.IsEnabled = sg is not null;
        StretchedScalingBox.SelectedIndex = s.StretchedScaling switch { GpuScaling.DriverDefault => 1, GpuScaling.Stretch => 2, GpuScaling.Center => 3, _ => 0 };
        StretchedScalingBox.IsEnabled = sg is not null;

        var tm = _c.ToggleMonitor;
        ToggleTarget.Text = tm is null ? "" : $"Applies to {tm.FriendlyName}. Change it in Settings ▸ Display, or edit these here to use the monitor selected above.";
        var hk = s.Hotkeys.TryGetValue(HotkeyAction.ToggleNativeStretched, out var b) && b.Enabled && s.HotkeysEnabled ? b.Gesture : null;
        ToggleHotkey.Text = hk ?? "No hotkey";
        ToggleButton.IsEnabled = sg is not null;
    }

    private void OnNativeStretchedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _m is null) return;
        var s = App.Settings;
        var groups = _m.Resolutions.ToList();
        s.PreferredMonitorId = _m.StableId;
        s.NativeMode = NativeBox.SelectedIndex <= 0 ? null : new StoredMode { Width = groups[NativeBox.SelectedIndex - 1].Width, Height = groups[NativeBox.SelectedIndex - 1].Height };
        var order = StretchedBox.Tag as List<ResolutionGroup> ?? groups;
        if (StretchedBox.SelectedIndex <= 0) s.StretchedMode = null;
        else
        {
            var g = order[StretchedBox.SelectedIndex - 1];
            int hz = 0;
            if (ReferenceEquals(sender, StretchedHzBox) && StretchedHzBox.SelectedIndex > 0 && StretchedHzBox.SelectedIndex - 1 < g.RefreshRates.Count)
                hz = g.RefreshRates[StretchedHzBox.SelectedIndex - 1];
            else if (!ReferenceEquals(sender, StretchedBox) && s.StretchedMode is { } old && old.Width == g.Width && old.Height == g.Height)
                hz = old.RefreshHz;
            s.StretchedMode = new StoredMode { Width = g.Width, Height = g.Height, RefreshHz = hz };
        }
        s.StretchedScaling = StretchedScalingBox.SelectedIndex switch { 1 => GpuScaling.DriverDefault, 2 => GpuScaling.Stretch, 3 => GpuScaling.Center, _ => GpuScaling.DontChange };
        _c.SaveSoon();
        _c.UpdateTray();
        _updating = true;
        try { RefreshNativeStretched(); } finally { _updating = false; }
    }

    private async void OnToggleNow(object sender, RoutedEventArgs e) => await _c.ToggleNativeStretchedAsync();

    // ------------------------------------------------------------ custom

    private void OnCustomKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter) { OnCustomFind(sender, e); e.Handled = true; }
    }

    private void OnCustomFind(object sender, RoutedEventArgs e)
    {
        if (_m is null) return;
        if (!ResolutionParser.TryParse(CustomBox.Text, out int w, out int h, out int hz))
        {
            CustomResult.Text = "Type a resolution like 1440x1080, optionally with @240 for the refresh rate.";
            return;
        }
        var g = _m.FindResolution(w, h);
        if (g is null)
        {
            var aspect = (double)w / h;
            var near = _m.Resolutions.OrderBy(x => Math.Abs((double)x.Width / x.Height - aspect) * 4 + Math.Abs(x.Width * x.Height - w * h) / (double)(w * h)).Take(3).Select(x => x.Text);
            CustomResult.Text = $"{w}×{h} isn't offered by the driver for {_m.FriendlyName}. Closest available: {string.Join(", ", near)}.";
            return;
        }
        _updating = true;
        SearchBox.Text = "";
        AspectBox.SelectedIndex = 0;
        _updating = false;
        ApplyFilter();
        if (hz > 0) _wantedHz = hz;
        HzBox.SelectedItem = null;
        SelectResolution(w, h);
        if (hz > 0 && !g.RefreshRates.Contains(hz))
            CustomResult.Text = $"{w}×{h} is available, but not at {hz} Hz. Supported: {string.Join(", ", g.RefreshRates)} Hz.";
        else
            CustomResult.Text = $"{w}×{h} is available (up to {g.MaxRefresh} Hz). It's selected above — press Apply.";
    }

    private static string ScalingName(GpuScaling s) => s switch
    {
        GpuScaling.Stretch => "stretch to fill",
        GpuScaling.Center => "centered",
        _ => "driver / monitor default",
    };
}
