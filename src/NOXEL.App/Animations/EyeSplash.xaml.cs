// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Noxel.App.Animations;

/// <summary>
/// "The Awakening", NOXEL's intro. The approved N Eye artwork (assets noxel-mark-body.png and noxel-mark-star.png,
/// cut from the app icon without redrawing) is revealed by animated geometry; nothing is approximated with paths.
///
/// Timeline (seconds)
///   0.00–0.40  Darkness: a soft lavender line grows along the eye's own diagonal axis.
///   0.40–1.20  Formation: the eye opens outward from that line (upper and lower contours), settling in scale.
///   1.20–1.80  Awakening: the four-point pupil fades and turns in, with a faint glow behind it.
///   1.80–2.40  Reveal: "NOXEL" fades in and rises a few pixels under the symbol, then holds.
///   2.40–3.20  Hand-off: the logo shrinks toward the title-bar wordmark while the black backdrop fades and the real
///              interface (already loaded underneath) fades in. Only starts once the app is ready; until then the
///              pupil glow breathes slowly.
///
/// Every animation is a composition KeyFrameAnimation: it runs on the compositor thread, is time-based (smooth at
/// any refresh rate), costs no UI-thread work per frame and allocates nothing per frame.
/// </summary>
public sealed partial class EyeSplash : UserControl
{
    // Geometry of the mark image (pixels of noxel-mark-body.png, 862 x 468).
    private const double ArtW = 862, ArtH = 468;
    private const double AxisAngle = 19.38;                       // lower-left tip (35,370) to upper-right tip (845,85)
    private static readonly Vector2 StarCenter = new(426f / 862f, 262f / 468f);

    private const float FormEnd = 2.4f, HandoffSeconds = 0.8f;

    private Compositor? _c;
    private bool _started, _formed, _backendReady, _handingOff, _finished;
    private Visual? _stage, _reveal, _line, _body, _star, _halo, _word, _backdrop, _hint;
    private InsetClip? _clip;
    private float _d;

    public event EventHandler? Finished;
    /// <summary>Raised once the starting state is set and the timeline is running (the window can be shown now).</summary>
    public event EventHandler? Started;
    /// <summary>Raised when the hand-off starts: the host fades the real interface in at the same time.</summary>
    public event EventHandler? HandoffStarting;
    /// <summary>Where the title-bar wordmark sits (center, in this control's coordinates), for the hand-off motion.</summary>
    public Func<global::Windows.Foundation.Point?>? HandoffTarget { get; set; }

    public EyeSplash()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PointerPressed += (_, _) => Skip();
        KeyDown += (_, _) => Skip();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;
        Focus(FocusState.Programmatic);
        Layout(Math.Max(ActualWidth, 400), Math.Max(ActualHeight, 300));
        // Start after the first layout pass so element sizes (and composition center points) are final.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Begin);
    }

    // ----------------------------------------------------------------- layout

    private void Layout(double width, double height)
    {
        double w = Math.Clamp(Math.Min(width * 0.42, height * 0.62 * ArtW / ArtH), 220, 560);
        double h = w * ArtH / ArtW;
        double d = Math.Ceiling(Math.Sqrt(w * w + h * h));
        _d = (float)d;
        MarkHost.Width = MarkHost.Height = d;
        AxisHost.Width = AxisHost.Height = d;
        RevealHost.Width = RevealHost.Height = d;
        Body.Width = Star.Width = w;
        Body.Height = Star.Height = h;
        Line.Width = w * 0.98;
        AxisRotate.Angle = -AxisAngle;
        BodyUnrotate.Angle = AxisAngle;
        Halo.Width = Halo.Height = w * 0.5;
        Halo.Margin = new Thickness((StarCenter.X - 0.5) * w * 2, (StarCenter.Y - 0.5) * h * 2, 0, 0);
        Wordmark.FontSize = Math.Round(w * 0.082);
        Wordmark.Margin = new Thickness(0, -(d - h) / 2 + h * 0.16, 0, 0);
        Stage.Margin = new Thickness(0, 0, 0, -((d - h) / 2)); // optical centering: the square host has empty bands
    }

    // ----------------------------------------------------------------- animation

    private Visual V(UIElement e)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        return ElementCompositionPreview.GetElementVisual(e);
    }

    private static Vector3 Center(FrameworkElement e) => new((float)e.ActualWidth / 2, (float)e.ActualHeight / 2, 0);

    private void Begin()
    {
        if (_finished) return;
        _c = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _stage = V(Stage); _reveal = V(RevealHost); _line = V(Line); _body = V(Body); _star = V(Star);
        _halo = V(Halo); _word = V(Wordmark); _backdrop = V(Backdrop); _hint = V(SkipHint);

        _line.CenterPoint = Center(Line);
        _body.CenterPoint = Center(Body);
        _star.CenterPoint = new Vector3((float)Star.ActualWidth * StarCenter.X, (float)Star.ActualHeight * StarCenter.Y, 0);
        _halo.CenterPoint = Center(Halo);
        _stage.CenterPoint = Center(Stage);

        // Starting state: everything dark, the reveal band closed to a sliver around the axis.
        _clip = _c.CreateInsetClip(0, _d / 2 - 1, 0, _d / 2 - 1);
        _reveal.Clip = _clip;
        _line.Opacity = 0; _line.Scale = new Vector3(0, 1, 1);
        _body.Opacity = 0; _body.Scale = new Vector3(0.965f, 0.965f, 1);
        _star.Opacity = 0; _star.Scale = new Vector3(0.35f, 0.35f, 1); _star.RotationAngleInDegrees = -45;
        _halo.Opacity = 0; _halo.Scale = new Vector3(0.6f, 0.6f, 1);
        _word.Opacity = 0; _word.Properties.InsertVector3("Translation", new Vector3(0, 10, 0));
        _hint.Opacity = 0;

        var outQuint = _c.CreateCubicBezierEasingFunction(new Vector2(0.22f, 1f), new Vector2(0.36f, 1f));
        var inOut = _c.CreateCubicBezierEasingFunction(new Vector2(0.65f, 0f), new Vector2(0.35f, 1f));
        var outCubic = _c.CreateCubicBezierEasingFunction(new Vector2(0.33f, 1f), new Vector2(0.68f, 1f));

        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);

        // Phase 1 — Darkness (0.0–0.4): the line grows from the centre along the axis.
        Run(_line, "Scale.X", 0, 1, 0.10f, 0.55f, outQuint);
        // One track per property: a second StartAnimation on the same property would replace the first.
        Track(_line, "Opacity", 1.20f, (0.10f, 0, null), (0.50f, 0.95f, outCubic), (0.75f, 0.95f, null), (1.20f, 0, outCubic));

        // Phase 2 — Formation (0.4–1.2): the eye opens outward from the line, upper and lower contours together.
        Run(_clip, "TopInset", _d / 2 - 1, 0, 0.40f, 0.80f, inOut);
        Run(_clip, "BottomInset", _d / 2 - 1, 0, 0.40f, 0.80f, inOut);
        Run(_body, "Opacity", 0, 1, 0.40f, 0.45f, outCubic);
        RunScale(_body, 0.965f, 1f, 0.40f, 0.85f, outQuint);

        // Phase 3 — Awakening (1.2–1.8): the four-point pupil turns in and brightens; a restrained glow behind it.
        Run(_star, "Opacity", 0, 1, 1.20f, 0.50f, outCubic);
        RunScale(_star, 0.35f, 1f, 1.20f, 0.60f, outQuint);
        Run(_star, "RotationAngleInDegrees", -45, 0, 1.20f, 0.60f, outQuint);
        Track(_halo, "Opacity", 2.40f, (1.25f, 0, null), (1.80f, 0.55f, outCubic), (1.85f, 0.55f, null), (2.40f, 0.32f, inOut));
        RunScale(_halo, 0.6f, 1f, 1.25f, 0.70f, outQuint);

        // Phase 4 — Reveal (1.8–2.4): the name fades in and rises a few pixels.
        Run(_word, "Opacity", 0, 1, 1.80f, 0.50f, outCubic);
        var rise = _c.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, 10, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, outQuint);
        rise.DelayTime = TimeSpan.FromSeconds(1.80); rise.Duration = TimeSpan.FromSeconds(0.55);
        rise.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        _word.StartAnimation("Translation", rise);
        Run(_hint, "Opacity", 0, 0.55f, 0.60f, 0.60f, outCubic);

        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(OnFormed);

        // Skipped before the first frame: show the finished logo instead of replaying the formation.
        if (_formed) { JumpToFormed(); if (_backendReady) StartHandoff(quick: true); }

        // Lift the curtain two UI frames later (the animations are live in the compositor by then), and report
        // Started two frames after that, when the first real frame is on screen. Phase 1 opens with 0.1 s of darkness.
        int frames = 0;
        void OnRendering(object? s, object a)
        {
            frames++;
            if (frames == 2) Curtain.Opacity = 1;
            if (frames < 4) return;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            Started?.Invoke(this, EventArgs.Empty);
        }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }

    private void Run(CompositionObject target, string property, float from, float to, float delay, float duration, CompositionEasingFunction ease)
    {
        var a = _c!.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0, from);
        a.InsertKeyFrame(1, to, ease);
        a.DelayTime = TimeSpan.FromSeconds(delay);
        a.Duration = TimeSpan.FromSeconds(duration);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        target.StartAnimation(property, a);
    }

    /// <summary>One animation from t=0 holding several (time, value) keys; the value before the first key is 0.</summary>
    private void Track(CompositionObject target, string property, float total, params (float t, float v, CompositionEasingFunction? ease)[] keys)
    {
        var a = _c!.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0, 0);
        foreach (var (t, v, ease) in keys)
        {
            if (ease is null) a.InsertKeyFrame(t / total, v);
            else a.InsertKeyFrame(t / total, v, ease);
        }
        a.Duration = TimeSpan.FromSeconds(total);
        target.StartAnimation(property, a);
    }

    private void RunScale(Visual v, float from, float to, float delay, float duration, CompositionEasingFunction ease)
    {
        var a = _c!.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0, new Vector3(from, from, 1));
        a.InsertKeyFrame(1, new Vector3(to, to, 1), ease);
        a.DelayTime = TimeSpan.FromSeconds(delay);
        a.Duration = TimeSpan.FromSeconds(duration);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Scale", a);
    }

    private void OnFormed()
    {
        if (_finished || _formed) return;
        _formed = true;
        if (_backendReady) StartHandoff();
        else Breathe();
    }

    /// <summary>The app is still loading: the pupil glow breathes slowly (one looping compositor animation).</summary>
    private void Breathe()
    {
        if (_c is null || _halo is null) return;
        var ease = _c.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
        var a = _c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0, 0.32f);
        a.InsertKeyFrame(0.5f, 0.5f, ease);
        a.InsertKeyFrame(1, 0.32f, ease);
        a.Duration = TimeSpan.FromSeconds(2.4);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        _halo.StartAnimation("Opacity", a);
    }

    /// <summary>Called by the window once the app has finished initializing.</summary>
    public void BackendReady()
    {
        _backendReady = true;
        if (_formed) StartHandoff();
    }

    /// <summary>Click or key: jump to the finished logo, or hand off straight away if the app is ready.</summary>
    public void Skip()
    {
        if (_finished || _handingOff) return;
        if (!_formed) JumpToFormed();
        if (_backendReady) StartHandoff(quick: true);
    }

    private void JumpToFormed()
    {
        _formed = true;
        if (_c is null) return; // not started yet: nothing to jump
        foreach (var (v, p) in new (CompositionObject?, string)[] {
                     (_line, "Opacity"), (_line, "Scale.X"), (_body, "Opacity"), (_body, "Scale"), (_star, "Opacity"), (_star, "Scale"),
                     (_star, "RotationAngleInDegrees"), (_halo, "Opacity"), (_halo, "Scale"), (_word, "Opacity"), (_word, "Translation"),
                     (_clip, "TopInset"), (_clip, "BottomInset") })
            v?.StopAnimation(p);
        _line!.Opacity = 0;
        _body!.Opacity = 1; _body.Scale = Vector3.One;
        _star!.Opacity = 1; _star.Scale = Vector3.One; _star.RotationAngleInDegrees = 0;
        _halo!.Opacity = 0.32f; _halo.Scale = Vector3.One;
        _word!.Opacity = 1; _word.Properties.InsertVector3("Translation", Vector3.Zero);
        _clip!.TopInset = _clip.BottomInset = 0;
        if (!_backendReady) Breathe();
    }

    private void StartHandoff(bool quick = false)
    {
        if (_handingOff || _finished) return;
        _handingOff = true;
        HandoffStarting?.Invoke(this, EventArgs.Empty);
        if (_c is null || _stage is null) { Finish(); return; }

        float dur = quick ? 0.35f : HandoffSeconds;
        var inOut = _c.CreateCubicBezierEasingFunction(new Vector2(0.65f, 0f), new Vector2(0.35f, 1f));
        var outCubic = _c.CreateCubicBezierEasingFunction(new Vector2(0.33f, 1f), new Vector2(0.68f, 1f));

        // Shrink toward the title-bar wordmark: same brand, now in its place in the app.
        var stageCenter = Stage.TransformToVisual(this).TransformPoint(new global::Windows.Foundation.Point(Stage.ActualWidth / 2, Stage.ActualHeight / 2));
        var target = HandoffTarget?.Invoke() ?? new global::Windows.Foundation.Point(ActualWidth / 2, ActualHeight / 2);
        var delta = new Vector3((float)(target.X - stageCenter.X), (float)(target.Y - stageCenter.Y), 0);

        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var move = _c.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1, delta, inOut);
        move.Duration = TimeSpan.FromSeconds(dur);
        _stage.StartAnimation("Translation", move);
        RunScale(_stage, 1f, 0.16f, 0, dur, inOut);
        Run(_stage, "Opacity", 1, 0, dur * 0.35f, dur * 0.65f, outCubic);
        Run(_backdrop!, "Opacity", 1, 0, dur * 0.15f, dur * 0.85f, inOut);
        Run(_hint!, "Opacity", _hint!.Opacity, 0, 0, 0.2f, outCubic);
        _halo?.StopAnimation("Opacity");
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(Finish);
        // The control's own background is the backdrop now; let the fading Backdrop visual show the app through.
        Root.Background = null;
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        Curtain.Opacity = 1;
        Visibility = Visibility.Collapsed;
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
