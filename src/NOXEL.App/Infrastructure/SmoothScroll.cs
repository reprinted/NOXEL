// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Noxel.App.Services;

namespace Noxel.App.Infrastructure;

/// <summary>
/// NOXEL's smooth scrolling for page ScrollViews.
///
/// Measured behaviour that shaped this design: WinUI's ScrollView already animates mouse-wheel input itself
/// (a short ~150 ms glide) and handles it on the compositor, before XAML sees any pointer event. So:
///  • Smooth scrolling OFF → the ScrollView is left completely native.
///  • Smooth scrolling ON  → the ScrollView ignores mouse-wheel input (IgnoredInputKinds = MouseWheel) and NOXEL
///    scrolls instead: each full wheel notch retargets one eased glide (180–320 ms, ease-out) that the compositor
///    runs on the ScrollView's InteractionTracker — no timers or per-frame UI-thread work. Notches that arrive
///    mid-glide extend the target; reversing direction restarts from the current position so it never feels floaty.
///    High-resolution wheel deltas (smaller than a notch — what precision touchpads and free-spinning wheels send)
///    are applied immediately 1:1, so touchpad tracking and Windows' own touchpad momentum are preserved.
/// Touch and pen are never affected (they don't use the wheel path), and keyboard scrolling is untouched.
/// </summary>
public static class SmoothScroll
{
    private sealed class State
    {
        public double Target = double.NaN;
        public int Direction;
        public int CorrelationId = -1;
    }

    private static readonly ConditionalWeakTable<ScrollView, State> States = new();

    [DllImport("user32.dll")] private static extern bool SystemParametersInfoW(uint action, uint param, out uint value, uint ini);
    private const uint SPI_GETWHEELSCROLLLINES = 0x0068;

    public static bool Active => App.Controller is not null && App.Settings.SmoothScrolling && ThemeService.SmoothScrollingAllowed(App.Settings);

    public static void Attach(ScrollView sv)
    {
        if (States.TryGetValue(sv, out _)) return;
        States.Add(sv, new State());
        sv.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnWheel), handledEventsToo: true);
        sv.ScrollAnimationStarting += OnAnimationStarting;
        sv.ScrollCompleted += (s, e) =>
        {
            if (States.TryGetValue(s, out var st) && e.CorrelationId == st.CorrelationId) { st.Target = double.NaN; st.Direction = 0; }
        };
        sv.Loaded += (_, _) => Configure(sv);
        Configure(sv);
    }

    /// <summary>Call after the smooth-scrolling or motion settings change.</summary>
    public static void ApplySettings()
    {
        foreach (var (sv, _) in States) Configure(sv);
    }

    private static void Configure(ScrollView sv) =>
        sv.IgnoredInputKinds = Active ? ScrollingInputKinds.MouseWheel : ScrollingInputKinds.None;

    private static double PixelsPerNotch()
    {
        uint lines = 3;
        try { SystemParametersInfoW(SPI_GETWHEELSCROLLLINES, 0, out lines, 0); } catch { }
        if (lines == 0) lines = 3;
        if (lines == uint.MaxValue) return 600; // "one screen at a time"
        return Math.Clamp(lines, 1u, 12u) * 38.0;
    }

    private static void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollView sv || !States.TryGetValue(sv, out var st)) return;
        if (sv.IgnoredInputKinds != ScrollingInputKinds.MouseWheel) return; // native mode: ScrollView handles it
        var pt = e.GetCurrentPoint(sv);
        if (pt.Properties.IsHorizontalMouseWheel) return;
        if (ConsumedInside(e.OriginalSource as DependencyObject, sv, e.Handled)) return;

        int delta = pt.Properties.MouseWheelDelta;
        if (delta == 0 || sv.ScrollableHeight <= 0) return;
        int dir = delta > 0 ? -1 : 1;
        double pixels = PixelsPerNotch() * Math.Abs(delta) / 120.0;
        e.Handled = true;

        if (Math.Abs(delta) % 120 != 0)
        {
            // High-resolution input (precision touchpad / free-spin wheel): follow the finger exactly.
            st.Target = double.NaN; st.Direction = 0;
            st.CorrelationId = sv.ScrollTo(sv.HorizontalOffset, Math.Clamp(sv.VerticalOffset + dir * pixels, 0, sv.ScrollableHeight),
                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
            return;
        }

        double from = double.IsNaN(st.Target) || st.Direction != dir ? sv.VerticalOffset : st.Target;
        double target = Math.Clamp(from + dir * pixels, 0, sv.ScrollableHeight);
        st.Target = target;
        st.Direction = dir;
        st.CorrelationId = sv.ScrollTo(sv.HorizontalOffset, target,
            new ScrollingScrollOptions(ScrollingAnimationMode.Enabled, ScrollingSnapPointsMode.Ignore));
    }

    /// <summary>NOXEL's eased curve for its own wheel glides.</summary>
    private static void OnAnimationStarting(ScrollView sv, ScrollingScrollAnimationStartingEventArgs e)
    {
        if (!States.TryGetValue(sv, out var st) || e.CorrelationId != st.CorrelationId) return;
        if (e.Animation is not Vector3KeyFrameAnimation stock) return;
        var c = stock.Compositor;
        double distance = Math.Abs(e.EndPosition.Y - e.StartPosition.Y);
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, new Vector3((float)e.EndPosition.X, (float)e.EndPosition.Y, 0f),
            c.CreateCubicBezierEasingFunction(new Vector2(0.15f, 0.85f), new Vector2(0.25f, 1f)));
        a.Duration = TimeSpan.FromMilliseconds(Math.Clamp(150 + distance * 0.35, 180, 320));
        e.Animation = a;
    }

    private static bool ConsumedInside(DependencyObject? source, ScrollView owner, bool handled)
    {
        for (var d = source; d is not null && !ReferenceEquals(d, owner); d = VisualTreeHelper.GetParent(d))
        {
            switch (d)
            {
                case ScrollViewer inner when inner.ScrollableHeight > 0 && inner.VerticalScrollMode != ScrollMode.Disabled:
                case ScrollView innerView when innerView.ScrollableHeight > 0:
                    return true; // a nested list scrolls itself
                case NumberBox or Slider when handled:
                    return true;
                case ComboBox { IsDropDownOpen: true }:
                    return true;
            }
        }
        return false;
    }
}
