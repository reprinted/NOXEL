// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Noxel.App.Animations;
using Noxel.App.Infrastructure;
using Noxel.App.Services;
using Noxel.Core.Hotkeys;
using Noxel.Core.Presets;

namespace Noxel.App.Views;

/// <summary>Small window sharing the same process and backend: look on/off, preset picker, pin, back to full view.</summary>
public sealed partial class CompactWindow : Window
{
    private readonly NoxelController _c = App.Controller!;
    private bool _updating;

    public CompactWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragArea);
        WindowHelper.SetIcon(this);
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.IsResizable = false;
            op.IsMaximizable = false;
            op.IsMinimizable = false;
            op.IsAlwaysOnTop = App.Settings.CompactAlwaysOnTop;
        }
        WindowHelper.RestorePlacement(this, App.Settings.CompactPlacement, 420, 150, resize: false);
        double s = WindowHelper.Scale(this);
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32((int)(420 * s), (int)(150 * s)));
        ApplyTheme();
        PinButton.IsChecked = App.Settings.CompactAlwaysOnTop;

        _c.ColorChanged += Refresh;
        _c.MonitorsChanged += Refresh;
        _c.PresetsChanged += Refresh;
        AppWindow.Closing += (_, _) => SavePlacement();
        Closed += (_, _) =>
        {
            _c.ColorChanged -= Refresh;
            _c.MonitorsChanged -= Refresh;
            _c.PresetsChanged -= Refresh;
        };
        Refresh();
        Root.Loaded += (_, _) => Motion.WindowContentEnter(Root);
        ContentLayout.SizeChanged += (_, _) => FitContentHeight();
    }

    private void FitContentHeight()
    {
        // The compact view stays narrow but grows vertically with Windows text size. ContentLayout is top-aligned in the
        // ScrollViewer: if it stretched, ActualHeight would be the viewport height and the window would grow to the screen.
        // On a short work area the surrounding ScrollViewer keeps the lower controls reachable.
        double scale = WindowHelper.Scale(this);
        int available = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea.Height;
        // Size the whole window (not the client area: ResizeClient adds the hidden caption and made it ~40 units taller
        // than the content). 12 units cover the bottom window border, so the controls sit tight like in 1.0.1.
        int height = Math.Min(available - (int)(16 * scale), (int)Math.Ceiling((ContentLayout.ActualHeight + 12) * scale));
        if (ContentLayout.ActualHeight > 0 && AppWindow.Size.Height != height)
            AppWindow.Resize(new global::Windows.Graphics.SizeInt32((int)(420 * scale), height));
    }

    public void ApplyTheme() => ThemeService.ApplyToWindow(this, Root, App.Settings);

    public void SavePlacement()
    {
        WindowHelper.SavePlacement(this, App.Settings.CompactPlacement);
        _c.SaveSoon();
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            PresetName.Text = _c.ActivePresetLabel;
            var m = _c.ToggleMonitor;
            var hk = App.Settings.Hotkeys.TryGetValue(HotkeyAction.ToggleColor, out var b) && b.Enabled ? b.Gesture : null;
            ModeText.Text = (m is null ? "No display" : $"{m.Current.Width}×{m.Current.Height} · {m.CurrentRefreshText}") +
                            (hk is null ? "" : $" · {hk}");
            ColorSwitch.IsOn = _c.ColorEnabled;
            var list = _c.Presets.Favorites.ToList();
            PresetBox.ItemsSource = list.Select(p => p.Name).ToList();
            PresetBox.Tag = list;
            int idx = list.FindIndex(p => p.Id == App.Settings.ActivePresetId);
            PresetBox.SelectedIndex = idx;
        }
        finally { _updating = false; }
    }

    private void OnColorToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _c.SetColorEnabled(ColorSwitch.IsOn);
    }

    private async void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || PresetBox.Tag is not List<Preset> list || PresetBox.SelectedIndex < 0) return;
        var p = list[PresetBox.SelectedIndex];
        if (p.Id == App.Settings.ActivePresetId && !_c.ActivePresetModified) return;
        await _c.ApplyPresetAsync(p.Id);
        Refresh();
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        bool on = PinButton.IsChecked == true;
        App.Settings.CompactAlwaysOnTop = on;
        if (AppWindow.Presenter is OverlappedPresenter op) op.IsAlwaysOnTop = on;
        ToolTipService.SetToolTip(PinButton, on ? "Stop keeping on top" : "Keep on top");
        _c.SaveSoon();
    }

    private void OnFull(object sender, RoutedEventArgs e)
    {
        SavePlacement();
        App.Current.CloseCompactAndShowMain();
    }
}
