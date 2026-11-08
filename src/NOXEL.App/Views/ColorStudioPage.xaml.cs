using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Noxel.App.Animations;
using Noxel.App.Infrastructure;
using Noxel.App.Services;
using Microsoft.UI.Xaml.Media.Animation;
using Noxel.Core.Color;

namespace Noxel.App.Views;

public sealed partial class ColorStudioPage : Page
{
    private readonly NoxelController _c = App.Controller!;
    private readonly Dictionary<ColorControl, Row> _rows = new();
    private DispatcherQueueTimer? _throttle;
    private ColorSettings _pending = ColorSettings.Default;
    private bool _updating, _dirty;

    private sealed class Row
    {
        public required ColorControl Control;
        public required ColorRange Range;
        public required Slider Slider;
        public required NumberBox Number;
        public required Button Reset;
        public required TextBlock Reason;
        public required Grid Container;
    }

    private static readonly (ColorControl C, string Name, string Desc, string Unit)[] Defs =
    {
        (ColorControl.Vibrance, "Digital vibrance", "Driver-level color intensity", ""),
        (ColorControl.Saturation, "Saturation", "Grayscale to vivid", "%"),
        (ColorControl.Brightness, "Brightness", "Lift or lower the image", ""),
        (ColorControl.Contrast, "Contrast", "Separation between light and dark", "%"),
        (ColorControl.Gamma, "Gamma", "Midtone curve", ""),
        (ColorControl.Temperature, "Temperature", "Cooler blue to warmer amber", ""),
    };

    public ColorStudioPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        Responsive.Attach(this);
        SmoothScroll.Attach(PageScroll);
        BuildRows();
        Loaded += (_, _) => { _c.ColorChanged += Refresh; _c.PresetsChanged += Refresh; Refresh(); };
        Unloaded += (_, _) =>
        {
            _c.ColorChanged -= Refresh;
            _c.PresetsChanged -= Refresh;
            if (CompareButton.IsChecked == true) _c.Compare(false);
            Flush();
        };
    }

    private static ColorRange RangeOf(ColorControl c) => c switch
    {
        ColorControl.Vibrance => ColorSettings.VibranceRange,
        ColorControl.Saturation => ColorSettings.SaturationRange,
        ColorControl.Brightness => ColorSettings.BrightnessRange,
        ColorControl.Contrast => ColorSettings.ContrastRange,
        ColorControl.Gamma => ColorSettings.GammaRange,
        _ => ColorSettings.TemperatureRange,
    };

    private static double Get(ColorSettings s, ColorControl c) => c switch
    {
        ColorControl.Vibrance => s.Vibrance,
        ColorControl.Saturation => s.Saturation,
        ColorControl.Brightness => s.Brightness,
        ColorControl.Contrast => s.Contrast,
        ColorControl.Gamma => s.Gamma,
        _ => s.Temperature,
    };

    private static ColorSettings With(ColorSettings s, ColorControl c, double v) => c switch
    {
        ColorControl.Vibrance => s with { Vibrance = v },
        ColorControl.Saturation => s with { Saturation = v },
        ColorControl.Brightness => s with { Brightness = v },
        ColorControl.Contrast => s with { Contrast = v },
        ColorControl.Gamma => s with { Gamma = v },
        _ => s with { Temperature = v },
    };

    private void BuildRows()
    {
        foreach (var (c, name, desc, unit) in Defs)
        {
            var range = RangeOf(c);
            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new StackPanel { Spacing = 1 };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            nameRow.Children.Add(new TextBlock { Text = name, Style = (Style)Application.Current.Resources["CardTitle"], FontSize = 17 });
            var badge = new Border { Style = (Style)Application.Current.Resources["Badge"], Margin = new Thickness(2, 0, 0, 0) };
            badge.Child = new TextBlock { Text = ColorCapabilities.EngineName(c).ToUpperInvariant(), Style = (Style)Application.Current.Resources["BadgeText"] };
            nameRow.Children.Add(badge);
            title.Children.Add(nameRow);
            title.Children.Add(new TextBlock { Text = desc, Style = (Style)Application.Current.Resources["FaintText"] });
            Grid.SetColumnSpan(title, 4);
            grid.Children.Add(title);

            var slider = new Slider
            {
                Minimum = range.Min, Maximum = range.Max, StepFrequency = range.Step,
                SmallChange = range.Step, LargeChange = range.Step * 10, Value = range.Default,
                TickPlacement = TickPlacement.None, IsThumbToolTipEnabled = true,
            };
            AutomationProperties.SetName(slider, name);
            Grid.SetRow(slider, 1);
            grid.Children.Add(slider);

            var number = new NumberBox
            {
                Minimum = range.Min, Maximum = range.Max, Value = range.Default, Width = 92,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                SmallChange = range.Step, LargeChange = range.Step * 10, VerticalAlignment = VerticalAlignment.Center,
                NumberFormatter = new global::Windows.Globalization.NumberFormatting.DecimalFormatter
                {
                    FractionDigits = c == ColorControl.Gamma ? 2 : 0, IntegerDigits = 1,
                    NumberRounder = new global::Windows.Globalization.NumberFormatting.IncrementNumberRounder { Increment = range.Step },
                },
            };
            AutomationProperties.SetName(number, name + " value");
            Grid.SetRow(number, 1); Grid.SetColumn(number, 1);
            grid.Children.Add(number);
            if (unit.Length > 0)
            {
                var u = new TextBlock { Text = unit, Style = (Style)Application.Current.Resources["MutedText"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-6, 0, 0, 0) };
                Grid.SetRow(u, 1); Grid.SetColumn(u, 2);
                grid.Children.Add(u);
            }

            var reset = new Button { Style = (Style)Application.Current.Resources["IconButton"], Content = new FontIcon { Glyph = "", FontSize = 13 }, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(reset, "Reset " + name);
            ToolTipService.SetToolTip(reset, "Reset to default");
            Grid.SetRow(reset, 1); Grid.SetColumn(reset, 3);
            grid.Children.Add(reset);

            var reason = new TextBlock { Style = (Style)Application.Current.Resources["FaintText"], Visibility = Visibility.Collapsed };
            reason.Foreground = (Microsoft.UI.Xaml.Media.Brush)Resources.ThemeDictionariesLookup("NoxelWarning");
            Grid.SetRow(reason, 2); Grid.SetColumnSpan(reason, 4);
            grid.Children.Add(reason);

            var row = new Row { Control = c, Range = range, Slider = slider, Number = number, Reset = reset, Reason = reason, Container = grid };
            slider.ValueChanged += (_, e) => OnValue(row, e.NewValue, fromSlider: true);
            slider.DoubleTapped += (_, _) => OnValue(row, range.Default, fromSlider: false);
            number.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) OnValue(row, e.NewValue, fromSlider: false); };
            reset.Click += (_, _) => OnValue(row, range.Default, fromSlider: false);
            _rows[c] = row;
            Rows.Children.Add(grid);
        }
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            var caps = _c.ColorCaps;
            var s = _c.WorkingColor;
            _pending = s;
            _dirty = false;
            MasterSwitch.IsOn = _c.ColorEnabled;
            LiveSwitch.IsOn = App.Settings.LivePreview;
            HdrBar.IsOpen = caps.HdrActive;
            bool matrixReasonShown = false;
            foreach (var row in _rows.Values)
            {
                bool ok = caps.IsAvailable(row.Control);
                string reason = ok ? "" : caps.ReasonFor(row.Control);
                if (!ok && ColorCapabilities.EngineName(row.Control) == "Color matrix")
                {
                    if (matrixReasonShown) reason = "Unavailable for the same reason as Saturation.";
                    matrixReasonShown = true;
                }
                double v = Get(s, row.Control);
                GlideTo(row.Slider, v);
                row.Number.Value = v;
                row.Slider.IsEnabled = row.Number.IsEnabled = row.Reset.IsEnabled = ok;
                row.Container.Opacity = ok ? 1 : 0.6;
                row.Reason.Text = reason;
                row.Reason.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            }
            LookName.Text = _c.ActivePresetLabel;
            LookSummary.Text = s.Summary();
            AppliedText.Text = _c.ColorEnabled ? "Applied to all displays" : "Effects are off";
            ApplyButton.Visibility = App.Settings.LivePreview && _c.ColorEnabled ? Visibility.Collapsed : Visibility.Visible;
            ApplyButton.Content = _c.ColorEnabled ? "Apply changes" : "Apply look";
            var active = _c.ActivePreset;
            UpdatePresetButton.Visibility = active is { IsBuiltIn: false } && _c.ActivePresetModified ? Visibility.Visible : Visibility.Collapsed;
            UpdatePresetButton.Content = active is null ? "" : $"Update \"{active.Name}\"";
            BuildEngineList(caps);
            RetryButton.Visibility = caps.MatrixAvailable && caps.RampAvailable && caps.VibranceAvailable ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _updating = false; }
    }

    private void BuildEngineList(ColorCapabilities caps)
    {
        EngineList.Children.Clear();
        void Add(string title, bool ok, string detail)
        {
            var g = new Grid { ColumnSpacing = 8, RowSpacing = 2 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition());
            var icon = new FontIcon { Glyph = ok ? "" : "", FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Resources.ThemeDictionariesLookup(ok ? "NoxelSuccess" : "NoxelWarning") };
            var t = new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyText"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var d = new TextBlock { Text = detail, Style = (Style)Application.Current.Resources["FaintText"] };
            Grid.SetColumn(t, 1); Grid.SetColumn(d, 1); Grid.SetRow(d, 1);
            g.Children.Add(icon); g.Children.Add(t); g.Children.Add(d);
            EngineList.Children.Add(g);
        }
        Add("Vibrance · NVIDIA driver", caps.VibranceAvailable, caps.VibranceAvailable ? "Available. Same setting as NVIDIA Control Panel ▸ Digital vibrance; 50 is the driver default." : caps.VibranceReason);
        Add("Color matrix · saturation, brightness, contrast, temperature", caps.MatrixAvailable, caps.MatrixAvailable ? "Available. One combined matrix applied by Windows to the whole desktop." : caps.MatrixReason);
        Add("Gamma · hardware gamma ramp", caps.RampAvailable, caps.RampAvailable ? "Available. Applied in the GPU output stage." : caps.RampReason);
    }

    /// <summary>
    /// When values change from outside (preset applied, reset), the thumb glides to the new value instead of
    /// jumping. The colour itself is applied immediately elsewhere; this is purely visual, and the slider's
    /// ValueChanged events during the glide are ignored so nothing is re-applied.
    /// </summary>
    private void GlideTo(Slider slider, double v)
    {
        if (!Motion.Enabled || !IsLoaded || Math.Abs(slider.Value - v) < 1e-6)
        {
            slider.Value = v;
            return;
        }
        var anim = new DoubleAnimation
        {
            From = slider.Value, To = v, Duration = TimeSpan.FromMilliseconds(220), EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, slider);
        Storyboard.SetTargetProperty(anim, "Value");
        sb.Children.Add(anim);
        _gliding++;
        sb.Completed += (_, _) =>
        {
            sb.Stop();
            _updatingGlide = true;
            slider.Value = v;
            _updatingGlide = false;
            _gliding--;
        };
        sb.Begin();
    }
    private int _gliding;
    private bool _updatingGlide;

    private void OnValue(Row row, double v, bool fromSlider)
    {
        if (_updating || _updatingGlide || (_gliding > 0 && fromSlider)) return;
        v = row.Range.Clamp(Math.Round(v / row.Range.Step) * row.Range.Step);
        _updating = true;
        try
        {
            if (Math.Abs(row.Slider.Value - v) > 1e-9) row.Slider.Value = v;
            if (Math.Abs(row.Number.Value - v) > 1e-9) row.Number.Value = v;
        }
        finally { _updating = false; }
        _pending = With(_pending, row.Control, v);
        _dirty = true;
        LookSummary.Text = _pending.Summary();
        if (App.Settings.LivePreview || !_c.ColorEnabled) ScheduleFlush();
    }

    /// <summary>Throttles live updates to ~30 per second while dragging.</summary>
    private void ScheduleFlush()
    {
        if (_throttle is null)
        {
            _throttle = DispatcherQueue.CreateTimer();
            _throttle.Interval = TimeSpan.FromMilliseconds(33);
            _throttle.IsRepeating = false;
            _throttle.Tick += (_, _) => Flush();
        }
        if (!_throttle.IsRunning) _throttle.Start();
    }

    private void Flush()
    {
        if (!_dirty) return;
        _dirty = false;
        var r = _c.SetWorkingColor(_pending);
        if (r is { Success: false } && !_warned)
        {
            _warned = true;
            _c.Raise("Color Studio", string.Join(" ", r.Problems.Distinct()), NoticeKind.Warning);
        }
    }
    private bool _warned;

    private void OnRetry(object sender, RoutedEventArgs e) => _c.RetryColorEngines();

    private void OnMasterToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        Flush();
        _c.SetColorEnabled(MasterSwitch.IsOn);
    }

    private void OnLiveToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        App.Settings.LivePreview = LiveSwitch.IsOn;
        _c.SaveSoon();
        Refresh();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        _dirty = true;
        Flush();
        if (!_c.ColorEnabled) _c.SetColorEnabled(true);
    }

    private void OnCompare(object sender, RoutedEventArgs e)
    {
        bool showOriginal = CompareButton.IsChecked == true;
        CompareButton.Content = showOriginal ? "Showing original — click to return" : "Compare with original";
        _c.Compare(showOriginal);
    }

    private void OnResetAll(object sender, RoutedEventArgs e)
    {
        _pending = ColorSettings.Default;
        _dirty = true;
        Flush();
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        CompareButton.IsChecked = false;
        _c.SetColorEnabled(false);
        _c.Raise("Original colors restored", "All color effects are off.", NoticeKind.Success);
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        Flush();
        var name = new TextBox { PlaceholderText = "Preset name", MaxLength = 48, Text = "" };
        var withDisplay = new CheckBox { Content = "Also save the current resolution and refresh rate", IsChecked = false };
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save as preset",
            Content = new StackPanel { Spacing = 12, Children = { name, withDisplay } },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        var n = string.IsNullOrWhiteSpace(name.Text) ? "My look" : name.Text.Trim();
        var p = _c.CaptureCurrent(n, withDisplay.IsChecked == true, includeColor: true);
        _c.Raise("Preset saved", $"\"{p.Name}\" is in your presets.", NoticeKind.Success);
        Refresh();
    }

    private void OnUpdatePreset(object sender, RoutedEventArgs e)
    {
        Flush();
        if (_c.ActivePreset is { IsBuiltIn: false } p)
        {
            _c.CaptureCurrent(p.Name, includeDisplay: false, includeColor: true, existing: p.Id);
            _c.Raise("Preset updated", $"\"{p.Name}\" now uses these colors.", NoticeKind.Success);
            Refresh();
        }
    }
}

internal static class ResourceExtensions
{
    /// <summary>Looks up a theme-dependent resource for the page's current theme.</summary>
    public static object ThemeDictionariesLookup(this ResourceDictionary _, string key)
    {
        var app = Application.Current.Resources;
        var theme = App.Main?.Content is FrameworkElement fe && fe.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        foreach (var md in app.MergedDictionaries)
        {
            if (md.ThemeDictionaries.TryGetValue(theme, out var d) && d is ResourceDictionary rd && rd.TryGetValue(key, out var v)) return v;
        }
        return app[key];
    }
}
