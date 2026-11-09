// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Noxel.App.Animations;

/// <summary>
/// The NOXEL intro: an original vector eye on a failing CRT. Everything is generated in code — eyelids are
/// Bézier curves whose control points are animated (a real blink and a real widening, not a fade), the iris
/// translates for the gaze, and the CRT layer is a pre-generated grain texture, pixel-exact scanlines,
/// glitch bands and RGB-split ghosts driven by one storyboard. No video, no per-frame CPU drawing.
/// Timeline (s): 0 stare · 0.45 blink · 0.80 look left · 1.22 look right · 1.70 center · 2.15 widen ·
/// 2.32 final stare · 2.80 final distortion · fade into the dashboard. The eye never closes at the end.
/// </summary>
public sealed partial class EyeSplash : UserControl
{
    private const double Total = 2.95;
    private const double CornerY = 250, LeftX = 130, RightX = 670;

    private readonly Random _rng = new(7193);
    private Storyboard? _main;
    private Storyboard? _idle;
    private bool _animationDone, _backendReady, _exiting, _built;
    private readonly List<Rectangle> _bands = new();

    public event EventHandler? Finished;

    private readonly record struct LidState(double UpperMid, double LowerMid, double Spread);

    private static readonly LidState Open = new(158, 330, 0);
    private static readonly LidState Closed = new(262, 256, -2);
    private static readonly LidState Wide = new(94, 380, 16);

    // Animated geometry handles
    private sealed class Curve
    {
        public PathFigure Figure = null!;
        public LineSegment? LeadIn;   // fill shapes: line to the left corner
        public BezierSegment Bezier = null!;
        public bool Upper;
        public double YOffset;        // folds sit above/below the lid line
        public double XPad;
    }
    private readonly List<Curve> _curves = new();

    public EyeSplash()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => BuildScanlines();
        PointerPressed += (_, _) => Skip();
        KeyDown += (_, _) => Skip();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        BuildGeometry();
        BuildIrisDetail();
        BuildVeins();
        Noise.Source = MakeNoise(360, 225);
        BuildScanlines();
        for (int i = 0; i < 7; i++)
        {
            var r = new Rectangle { Opacity = 0, Height = 2 + _rng.Next(16), RenderTransform = new TranslateTransform(), Fill = BandBrush(i) };
            _bands.Add(r);
            Bands.Children.Add(r);
        }
        Focus(FocusState.Programmatic);
        _main = BuildStoryboard(Math.Max(ActualHeight, 300), Math.Max(ActualWidth, 400));
        _main.Completed += (_, _) =>
        {
            _animationDone = true;
            if (_backendReady) Exit();
            else StartIdle();
        };
        _main.Begin();
    }

    /// <summary>Called when the app finished initializing; the intro then completes on its own schedule.</summary>
    public void BackendReady()
    {
        _backendReady = true;
        if (_animationDone) Exit();
    }

    public void Skip()
    {
        if (_exiting) return;
        if (!_backendReady)
        {
            // Jump to the final stare and wait there; never show a half-loaded dashboard.
            _main?.SkipToFill();
            return;
        }
        _main?.Stop();
        Exit(quick: true);
    }

    private void Exit(bool quick = false)
    {
        if (_exiting) return;
        _exiting = true;
        _idle?.Stop();
        var sb = new Storyboard();
        double d = quick ? 0.18 : 0.34;
        sb.Children.Add(Anim(this, "Opacity", 1, 0, d, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        sb.Children.Add(Anim(StageScale, "ScaleX", 1, 1.06, d, null));
        sb.Children.Add(Anim(StageScale, "ScaleY", 1, 1.06, d, null));
        sb.Completed += (_, _) => Cleanup();
        sb.Begin();
    }

    private void Cleanup()
    {
        _main?.Stop();
        _main = null;
        _idle = null;
        Noise.Source = null;
        Scanlines.Source = null;
        Bands.Children.Clear();
        _bands.Clear();
        _curves.Clear();
        Visibility = Visibility.Collapsed;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void StartIdle()
    {
        // Backend still loading: keep a quiet living grain until it is ready (cheap, independent animation).
        _idle = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var x = new DoubleAnimationUsingKeyFrames();
        var y = new DoubleAnimationUsingKeyFrames();
        for (int i = 0; i < 12; i++)
        {
            var t = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(i * 0.06));
            x.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = t, Value = _rng.Next(-60, 60) });
            y.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = t, Value = _rng.Next(-60, 60) });
        }
        Storyboard.SetTarget(x, NoiseT); Storyboard.SetTargetProperty(x, "X");
        Storyboard.SetTarget(y, NoiseT); Storyboard.SetTargetProperty(y, "Y");
        _idle.Children.Add(x); _idle.Children.Add(y);
        _idle.Begin();
    }

    // ----------------------------------------------------------------- geometry

    private void BuildGeometry()
    {
        UpperLid.Data = FillShape(upper: true);
        LowerLid.Data = FillShape(upper: false);
        UpperLine.Data = OpenCurve(upper: true, 0, 0);
        LowerLine.Data = OpenCurve(upper: false, 0, 0);
        UpperShadow.Data = OpenCurve(upper: true, 6, 0);
        UpperShadowWide.Data = OpenCurve(upper: true, 12, 0);
        LowerShadow.Data = OpenCurve(upper: false, -4, 0);
        UpperFold.Data = OpenCurve(upper: true, -44, 22);
        LowerFold.Data = OpenCurve(upper: false, 32, 14);
        Apply(Open);
    }

    private PathGeometry FillShape(bool upper)
    {
        double outer = upper ? -40 : 540;
        var fig = new PathFigure { StartPoint = new Point(-40, outer), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment { Point = new Point(-40, CornerY) });
        var lead = new LineSegment();
        fig.Segments.Add(lead);
        var bez = new BezierSegment();
        fig.Segments.Add(bez);
        fig.Segments.Add(new LineSegment { Point = new Point(840, CornerY) });
        fig.Segments.Add(new LineSegment { Point = new Point(840, outer) });
        _curves.Add(new Curve { Figure = fig, LeadIn = lead, Bezier = bez, Upper = upper });
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    private PathGeometry OpenCurve(bool upper, double yOffset, double xPad)
    {
        var fig = new PathFigure { IsClosed = false, IsFilled = false };
        var bez = new BezierSegment();
        fig.Segments.Add(bez);
        _curves.Add(new Curve { Figure = fig, Bezier = bez, Upper = upper, YOffset = yOffset, XPad = xPad });
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    /// <summary>Control points for a lid state. Cubic with both controls at y=c peaks at 0.25*corner + 0.75*c.</summary>
    private static (Point L, Point C1, Point C2, Point R) Points(LidState s, Curve c)
    {
        double lx = LeftX - s.Spread - c.XPad, rx = RightX + s.Spread + c.XPad;
        double cornerY = CornerY + (c.YOffset == 0 ? 0 : (c.Upper ? -8 : 8));
        double mid = c.Upper ? s.UpperMid : s.LowerMid;
        double cy = (mid + c.YOffset - 0.25 * cornerY) / 0.75;
        return (new Point(lx, cornerY), new Point(lx + (rx - lx) * 0.2, cy), new Point(lx + (rx - lx) * 0.8, cy), new Point(rx, cornerY));
    }

    private void Apply(LidState s)
    {
        foreach (var c in _curves)
        {
            var (l, c1, c2, r) = Points(s, c);
            if (c.LeadIn is not null) c.LeadIn.Point = l; else c.Figure.StartPoint = l;
            c.Bezier.Point1 = c1; c.Bezier.Point2 = c2; c.Bezier.Point3 = r;
        }
    }

    private void BuildIrisDetail()
    {
        var light = new PathGeometry();
        var dark = new PathGeometry();
        const double cx = 400, cy = 250;
        for (int i = 0; i < 150; i++)
        {
            double a = i / 150.0 * Math.PI * 2 + _rng.NextDouble() * 0.03;
            double r1 = 42 + _rng.NextDouble() * 10, r2 = 80 + _rng.NextDouble() * 16;
            double bend = (_rng.NextDouble() - 0.5) * 0.12;
            var f = new PathFigure { StartPoint = new Point(cx + r1 * Math.Cos(a), cy + r1 * Math.Sin(a)) };
            f.Segments.Add(new QuadraticBezierSegment
            {
                Point1 = new Point(cx + (r1 + r2) / 2 * Math.Cos(a + bend), cy + (r1 + r2) / 2 * Math.Sin(a + bend)),
                Point2 = new Point(cx + r2 * Math.Cos(a), cy + r2 * Math.Sin(a)),
            });
            (i % 3 == 0 ? dark : light).Figures.Add(f);
        }
        // crypts
        for (int i = 0; i < 14; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, r = 55 + _rng.NextDouble() * 30;
            var p = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            var f = new PathFigure { StartPoint = p };
            f.Segments.Add(new LineSegment { Point = new Point(p.X + 3 * Math.Cos(a + 1.4), p.Y + 3 * Math.Sin(a + 1.4)) });
            dark.Figures.Add(f);
        }
        FibersLight.Data = light;
        FibersDark.Data = dark;
    }

    private void BuildVeins()
    {
        var g = new PathGeometry();
        for (int i = 0; i < 14; i++)
        {
            bool left = i % 2 == 0;
            double y = 190 + _rng.NextDouble() * 120;
            var start = new Point(left ? 150 + _rng.NextDouble() * 30 : 650 - _rng.NextDouble() * 30, y);
            var f = new PathFigure { StartPoint = start };
            double x = start.X;
            for (int s = 0; s < 3; s++)
            {
                double nx = x + (left ? 1 : -1) * (25 + _rng.NextDouble() * 30);
                double ny = y + (_rng.NextDouble() - 0.5) * 26;
                f.Segments.Add(new QuadraticBezierSegment { Point1 = new Point((x + nx) / 2, y + (_rng.NextDouble() - 0.5) * 20), Point2 = new Point(nx, ny) });
                x = nx; y = ny;
            }
            g.Figures.Add(f);
        }
        Veins.Data = g;
    }

    // ----------------------------------------------------------------- CRT textures

    private WriteableBitmap MakeNoise(int w, int h)
    {
        var bmp = new WriteableBitmap(w, h);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            bool streak = _rng.NextDouble() < 0.025;
            for (int x = 0; x < w; x++)
            {
                // Mostly dark grain with occasional bright specks, like a weak analog signal.
                double r = _rng.NextDouble();
                int v = (int)(Math.Pow(r, 2.2) * 255);
                if (_rng.NextDouble() < 0.004) v = 255;
                if (streak) v = Math.Min(255, v + 70);
                int i = (y * w + x) * 4;
                px[i] = (byte)Math.Min(255, v + 18); // B (slight blue cast)
                px[i + 1] = (byte)v;
                px[i + 2] = (byte)Math.Max(0, v - 10);
                px[i + 3] = 255;
            }
        }
        using (var s = bmp.PixelBuffer.AsStream()) s.Write(px, 0, px.Length);
        bmp.Invalidate();
        return bmp;
    }

    private void BuildScanlines()
    {
        if (XamlRoot is null || ActualHeight < 2) return;
        double scale = XamlRoot.RasterizationScale;
        int h = Math.Max(2, (int)Math.Round(ActualHeight * scale));
        const int w = 4;
        var bmp = new WriteableBitmap(w, h);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            byte a = (y % 3 == 0) ? (byte)0x78 : (y % 3 == 1) ? (byte)0x18 : (byte)0;
            for (int x = 0; x < w; x++) px[(y * w + x) * 4 + 3] = a; // premultiplied black
        }
        using (var s = bmp.PixelBuffer.AsStream()) s.Write(px, 0, px.Length);
        bmp.Invalidate();
        Scanlines.Source = bmp;
    }

    private Brush BandBrush(int i)
    {
        Color tint = (i % 3) switch { 0 => Color.FromArgb(255, 0xFF, 0x3A, 0x5C), 1 => Color.FromArgb(255, 0x3A, 0xE6, 0xFF), _ => Color.FromArgb(255, 0xB0, 0x98, 0xFF) };
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        double s = _rng.NextDouble() * 0.3;
        b.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, tint.R, tint.G, tint.B), Offset = s });
        b.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x90, tint.R, tint.G, tint.B), Offset = s + 0.15 });
        b.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xB0, 0xF2, 0xF0, 0xFF), Offset = s + 0.3 });
        b.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x70, tint.R, tint.G, tint.B), Offset = s + 0.5 });
        b.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, tint.R, tint.G, tint.B), Offset = Math.Min(1, s + 0.75) });
        return b;
    }

    // ----------------------------------------------------------------- timeline

    private sealed class DTrack
    {
        public readonly DependencyObject Target; public readonly string Prop;
        public readonly List<(double T, double V, bool Discrete, EasingFunctionBase? E)> K = new();
        public DTrack(DependencyObject t, string p, double initial) { Target = t; Prop = p; K.Add((0, initial, true, null)); }
        public DTrack At(double t, double v, EasingFunctionBase? e = null) { K.Add((t, v, false, e)); return this; }
        public DTrack Snap(double t, double v) { K.Add((t, v, true, null)); return this; }
    }

    private sealed class PTrack
    {
        public readonly DependencyObject Target; public readonly string Prop;
        public readonly List<(double T, Point V, EasingFunctionBase? E)> K = new();
        public PTrack(DependencyObject t, string p) { Target = t; Prop = p; }
    }

    private Storyboard BuildStoryboard(double height, double width)
    {
        var sb = new Storyboard();
        var d = new List<DTrack>();
        var byKey = new Dictionary<(DependencyObject, string), DTrack>();
        // One animation per (target, property): a storyboard may not animate a property twice.
        DTrack T(DependencyObject o, string p, double init)
        {
            if (byKey.TryGetValue((o, p), out var existing)) return existing;
            var t = new DTrack(o, p, init);
            byKey[(o, p)] = t;
            d.Add(t);
            return t;
        }

        // ---- eyelids (real geometry) ----
        var lidKeys = new List<(double T, LidState S, EasingFunctionBase? E)>
        {
            (0, Open, null),
            (0.45, Open, null),
            (0.57, Closed, new QuadraticEase { EasingMode = EasingMode.EaseIn }),   // lids accelerate shut
            (0.62, Closed, null),
            (0.80, Open, new CubicEase { EasingMode = EasingMode.EaseOut }),       // and ease back open
            (2.15, Open, null),
            (2.29, Wide, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }), // sudden widening
            (Total, Wide, null),
        };
        // Lids follow the gaze very slightly (upper lid drops a touch when looking sideways).
        lidKeys.Add((1.12, Open with { UpperMid = Open.UpperMid + 5 }, new CubicEase { EasingMode = EasingMode.EaseInOut }));
        lidKeys.Add((1.62, Open with { UpperMid = Open.UpperMid + 5 }, new CubicEase { EasingMode = EasingMode.EaseInOut }));
        lidKeys.Add((1.95, Open, new CubicEase { EasingMode = EasingMode.EaseInOut }));
        lidKeys.Sort((a, b) => a.T.CompareTo(b.T));

        foreach (var c in _curves)
        {
            var tracks = new[]
            {
                c.LeadIn is not null ? new PTrack(c.LeadIn, "Point") : new PTrack(c.Figure, "StartPoint"),
                new PTrack(c.Bezier, "Point1"), new PTrack(c.Bezier, "Point2"), new PTrack(c.Bezier, "Point3"),
            };
            foreach (var (t, s, e) in lidKeys)
            {
                var (l, c1, c2, r) = Points(s, c);
                tracks[0].K.Add((t, l, e)); tracks[1].K.Add((t, c1, e)); tracks[2].K.Add((t, c2, e)); tracks[3].K.Add((t, r, e));
            }
            foreach (var pt in tracks) sb.Children.Add(BuildPoint(pt));
        }

        // ---- gaze ----
        var inOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
        T(Gaze, "X", 0).At(0.80, 0).At(1.10, -118, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.15 })
            .At(1.22, -118).At(1.60, 112, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.15 })
            .At(1.70, 112).At(1.95, 0, inOut).At(Total, 0);
        T(Gaze, "Y", 0).At(0.80, 0).At(1.10, 6, inOut).At(1.22, 6).At(1.60, 4, inOut).At(1.95, 0, inOut).At(Total, 0);

        // ---- pupil constricts on the widen, iris glows during the final stare ----
        T(PupilScale, "ScaleX", 1).At(2.15, 1).At(2.32, 0.74, new ExponentialEase { EasingMode = EasingMode.EaseOut }).At(Total, 0.8);
        T(PupilScale, "ScaleY", 1).At(2.15, 1).At(2.32, 0.74, new ExponentialEase { EasingMode = EasingMode.EaseOut }).At(Total, 0.8);
        T(IrisGlow, "Opacity", 0).At(2.32, 0).At(2.46, 0.6).At(2.78, 0.32);

        // ---- the rig lunges a little when the eye widens ----
        T(RigT, "ScaleX", 1).At(2.15, 1).At(2.30, 1.045, new ExponentialEase { EasingMode = EasingMode.EaseOut }).At(Total, 1.05)
            .Snap(2.80, 1.13).Snap(2.84, 1.02).Snap(2.87, 1.05);
        T(RigT, "ScaleY", 1).At(2.15, 1).At(2.30, 1.045, new ExponentialEase { EasingMode = EasingMode.EaseOut }).At(Total, 1.05)
            .Snap(2.80, 0.96).Snap(2.84, 1.06).Snap(2.87, 1.05);
        var rigX = T(RigT, "TranslateX", 0);
        var ghostR = T(GhostRed, "Opacity", 0);
        var ghostC = T(GhostCyan, "Opacity", 0);
        var ghostRX = T(GhostRedT, "X", 0);
        var ghostCX = T(GhostCyanT, "X", 0);
        var flicker = T(Flicker, "Opacity", 0);
        var noiseOp = T(Noise, "Opacity", 0.2);

        // ---- base interference level: calmer before the widen, then a burst ----
        noiseOp.At(1.70, 0.2).At(2.10, 0.11).At(2.18, 0.11);

        // ---- glitch events ----
        var events = new (double T, double S)[] { (0.13, 0.5), (0.31, 0.35), (0.98, 0.55), (1.33, 0.4), (1.47, 0.45), (2.24, 1.0), (2.30, 0.8), (2.37, 0.6), (2.80, 0.95) };
        foreach (var (t, s) in events)
        {
            double j = 16 * s;
            rigX.Snap(t, (_rng.NextDouble() < 0.5 ? -1 : 1) * j).Snap(t + 0.03, (_rng.NextDouble() - 0.5) * j).Snap(t + 0.065, 0);
            ghostR.Snap(t, 0.75 * s).Snap(t + 0.08, 0);
            ghostC.Snap(t, 0.75 * s).Snap(t + 0.08, 0);
            ghostRX.Snap(t, -8 * s).Snap(t + 0.08, 0);
            ghostCX.Snap(t, 8 * s).Snap(t + 0.08, 0);
            if (s >= 0.4) flicker.Snap(t, 0.22 * s).Snap(t + 0.035, 0);
            double baseNoise = t < 1.7 ? 0.2 : t < 2.15 ? 0.11 : 0.16;
            noiseOp.Snap(t, baseNoise + 0.22 * s).Snap(t + 0.09, baseNoise);
            int count = 1 + (int)Math.Round(s * 3);
            for (int b = 0; b < count; b++)
            {
                var band = _bands[_rng.Next(_bands.Count)];
                var bt = (TranslateTransform)band.RenderTransform;
                double y = (0.15 + _rng.NextDouble() * 0.7) * height;
                double tt = t + b * 0.012;
                T(band, "Opacity", 0).Snap(tt, 0.55 + 0.45 * s).Snap(tt + 0.05 + 0.04 * s, 0);
                T(bt, "Y", 0).Snap(tt, y).Snap(tt + 0.03, y + (_rng.NextDouble() - 0.5) * 30);
                T(bt, "X", 0).Snap(tt, (_rng.NextDouble() - 0.5) * width * 0.3);
            }
        }
        noiseOp.At(2.50, 0.16).Snap(2.52, 0.3).At(2.62, 0.16);
        T(Pulse, "Opacity", 0).At(2.40, 0).At(2.48, 0.07).At(2.66, 0);

        // ---- grain motion (discrete jumps every 50 ms) and the rolling CRT bar ----
        var nx = T(NoiseT, "X", 0);
        var ny = T(NoiseT, "Y", 0);
        for (double t = 0.05; t < Total; t += 0.05)
        {
            nx.Snap(t, _rng.Next(-60, 60));
            ny.Snap(t, _rng.Next(-60, 60));
        }
        T(RollBarT, "Y", -160).At(Total, height + 20);

        foreach (var track in d) sb.Children.Add(BuildDouble(track));
        foreach (var r in _bands) r.Width = width;
        return sb;
    }

    private static DoubleAnimationUsingKeyFrames BuildDouble(DTrack t)
    {
        var a = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true };
        foreach (var k in t.K.Select((k, i) => (k, i)).OrderBy(x => x.k.T).ThenBy(x => x.i).Select(x => x.k))
        {
            var kt = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Math.Max(0, k.T)));
            if (k.Discrete) a.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = kt, Value = k.V });
            else a.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = kt, Value = k.V, EasingFunction = k.E });
        }
        Storyboard.SetTarget(a, t.Target);
        Storyboard.SetTargetProperty(a, t.Prop);
        return a;
    }

    private static PointAnimationUsingKeyFrames BuildPoint(PTrack t)
    {
        var a = new PointAnimationUsingKeyFrames { EnableDependentAnimation = true };
        foreach (var k in t.K.OrderBy(k => k.T))
        {
            var kt = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(k.T));
            if (k.T == 0) a.KeyFrames.Add(new DiscretePointKeyFrame { KeyTime = kt, Value = k.V });
            else a.KeyFrames.Add(new EasingPointKeyFrame { KeyTime = kt, Value = k.V, EasingFunction = k.E });
        }
        Storyboard.SetTarget(a, t.Target);
        Storyboard.SetTargetProperty(a, t.Prop);
        return a;
    }

    private static DoubleAnimation Anim(DependencyObject target, string prop, double from, double to, double seconds, EasingFunctionBase? e)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromSeconds(seconds), EasingFunction = e, EnableDependentAnimation = true };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, prop);
        return a;
    }
}
