using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Noxel.App.Animations;
using Noxel.App.Infrastructure;
using Noxel.App.Services;
using Noxel.Core.Display;
using Noxel.Core.Presets;

namespace Noxel.App.Views;

public sealed partial class DashboardPage : Page
{
    private readonly NoxelController _c = App.Controller!;
    private bool _updating;
    private List<MonitorInfo> _monitors = new();
    private List<Preset> _presets = new();

    public DashboardPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        Responsive.Attach(this);
        SmoothScroll.Attach(PageScroll);
        Loaded += (_, _) =>
        {
            _c.MonitorsChanged += Refresh;
            _c.ColorChanged += Refresh;
            _c.PresetsChanged += Refresh;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _c.MonitorsChanged -= Refresh;
            _c.ColorChanged -= Refresh;
            _c.PresetsChanged -= Refresh;
        };
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            _monitors = _c.Monitors.Where(m => m.IsActive).ToList();
            MonitorBox.ItemsSource = _monitors.Select(m => m.DisplayLabel).ToList();
            var m = _c.SelectedMonitor;
            MonitorBox.SelectedIndex = m is null ? -1 : _monitors.FindIndex(x => x.StableId == m.StableId);
            MonitorBox.IsEnabled = _monitors.Count > 1;

            if (m is null)
            {
                ResText.Text = HzText.Text = MaxHzText.Text = NativeText.Text = "—";
                MonitorId.Text = "No active monitor detected.";
            }
            else
            {
                PrimaryBadge.Visibility = m.IsPrimary ? Visibility.Visible : Visibility.Collapsed;
                ConnBadge.Text = $"CONNECTED · {m.Connection.ToUpperInvariant()}";
                HdrBadge.Visibility = m.HdrSupported ? Visibility.Visible : Visibility.Collapsed;
                HdrText.Text = m.HdrEnabled ? "HDR ON" : "HDR OFF";
                MonitorId.Text = m.StableId;
                ResText.Text = $"{m.Current.Width} × {m.Current.Height}";
                ThumbRes.Text = $"{m.Current.Width}×{m.Current.Height}";
                HzText.Text = m.CurrentRefreshText;
                int max = m.MaxRefreshFor(m.Current.Width, m.Current.Height);
                MaxHzText.Text = max > 0 ? $"{max} Hz" : "—";
                NativeText.Text = m.HasPreferred ? $"{m.PreferredWidth} × {m.PreferredHeight}" : "Unknown";
                IccText.Text = string.IsNullOrEmpty(m.IccProfile) ? "Windows default" : m.IccProfile;
            }
            LookText.Text = _c.ColorEnabled ? $"On · {_c.ActivePresetLabel}" : "Off";

            // Presets
            _presets = _c.Presets.All.ToList();
            PresetBox.ItemsSource = _presets.Select(p => p.IsFavorite ? "★ " + p.Name : p.Name).ToList();
            PresetBox.SelectedIndex = _presets.FindIndex(p => p.Id == App.Settings.ActivePresetId);
            PresetSummary.Text = _c.ActivePreset is { } ap
                ? (_c.ActivePresetModified ? "Edited in Color Studio · " : "") + ap.Summary()
                : "Pick a preset to apply its display mode and colors.";

            FavoritesPanel.Children.Clear();
            var favs = _c.Presets.Favorites.ToList();
            NoFavorites.Visibility = favs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var f in favs.Take(6))
            {
                bool active = f.Id == App.Settings.ActivePresetId;
                var btn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Style = (Style)Application.Current.Resources["SubtleGlassButton"],
                    Tag = f.Id,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 10,
                        Children =
                        {
                            new FontIcon { Glyph = active ? "" : "", FontSize = 14 },
                            new TextBlock { Text = f.Name, FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal },
                        },
                    },
                };
                AutomationPropertiesHelper(btn, $"Apply favorite preset {f.Name}");
                Motion.SetHoverLift(btn, true);
                btn.Click += async (s, _) => await _c.ApplyPresetAsync((Guid)((Button)s).Tag);
                FavoritesPanel.Children.Add(btn);
            }

            // Quick actions
            var tm = _c.ToggleMonitor;
            var native = tm is null ? null : _c.NativeFor(tm);
            var stretched = tm is null ? null : _c.StretchedFor(tm);
            NativeTileText.Text = native is { } n ? $"{n.Width}×{n.Height}" : "Unknown";
            StretchedTileText.Text = stretched is { } st ? $"{st.Width}×{st.Height}" : "Not set";
            ColorTileText.Text = _c.ColorEnabled ? "On" : "Off";
            ColorTileIcon.Glyph = _c.ColorEnabled ? "" : "";
            PresetsTileText.Text = $"{_presets.Count} saved";
        }
        finally { _updating = false; }
    }

    private static void AutomationPropertiesHelper(DependencyObject o, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(o, name);

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || MonitorBox.SelectedIndex < 0) return;
        _c.SelectMonitor(_monitors[MonitorBox.SelectedIndex].StableId);
    }

    private async void OnPresetPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || PresetBox.SelectedIndex < 0) return;
        var p = _presets[PresetBox.SelectedIndex];
        if (p.Id == App.Settings.ActivePresetId && !_c.ActivePresetModified) return;
        await _c.ApplyPresetAsync(p.Id);
        Refresh();
    }

    private async void OnNative(object sender, RoutedEventArgs e) => await _c.SwitchToNativeAsync();
    private async void OnStretched(object sender, RoutedEventArgs e) => await _c.SwitchToStretchedAsync();
    private void OnToggleColor(object sender, RoutedEventArgs e) => _c.ToggleColor();
    private void OnOpenPresets(object sender, RoutedEventArgs e) => App.Main?.NavigateTo("Presets");
    private async void OnRestore(object sender, RoutedEventArgs e) => await _c.RestoreDefaultsAsync(_c.SelectedMonitor);
}
