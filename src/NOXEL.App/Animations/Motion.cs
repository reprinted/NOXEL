// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Noxel.App.Services;

namespace Noxel.App.Animations;

/// <summary>
/// NOXEL's animation system. Every effect is a short Composition animation (runs on the compositor thread,
/// no per-frame UI-thread work, nothing left running when idle) and every one checks the user's settings,
/// so turning animations off makes all of them instant.
/// </summary>
public static class Motion
{
    // ---------------- tokens ----------------
    // One timing scale for the whole app, shared with the "Awakening" intro (same curves, shorter durations).
    public static readonly TimeSpan Hover = TimeSpan.FromMilliseconds(140);      // hover lift
    public static readonly TimeSpan Press = TimeSpan.FromMilliseconds(100);      // press squeeze
    public static readonly TimeSpan PopTime = TimeSpan.FromMilliseconds(220);        // favourite star
    public static readonly TimeSpan Dialog = TimeSpan.FromMilliseconds(220);     // notices in / out
    public static readonly TimeSpan PageExit = TimeSpan.FromMilliseconds(120);   // outgoing page fades under the new one
    public static readonly TimeSpan Page = TimeSpan.FromMilliseconds(240);       // incoming page
    public static readonly TimeSpan Theme = TimeSpan.FromMilliseconds(300);      // theme / material cross-fade

    /// <summary>
    /// Easing, identical to the intro's: quintic-like ease-out for entrances and hover, cubic ease-out for fades,
    /// symmetric cubic ease-in-out for value changes and hand-offs. No overshoot anywhere.
    /// </summary>
    private static readonly Vector2 OutA = new(0.22f, 1f), OutB = new(0.36f, 1f);
    private static readonly Vector2 FadeA = new(0.33f, 1f), FadeB = new(0.68f, 1f);
    private static readonly Vector2 InOutA = new(0.65f, 0f), InOutB = new(0.35f, 1f);

    public static bool Enabled => App.Controller is not null && ThemeService.AnimationsAllowed(App.Settings);
    public static bool MotionBlur => Enabled && App.Settings.MotionBlur;

    private static Visual V(UIElement e)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        return ElementCompositionPreview.GetElementVisual(e);
    }

    private static CompositionEasingFunction EaseOut(Compositor c) => c.CreateCubicBezierEasingFunction(OutA, OutB);
    private static CompositionEasingFunction EaseFade(Compositor c) => c.CreateCubicBezierEasingFunction(FadeA, FadeB);
    public static CompositionEasingFunction EaseInOut(Compositor c) => c.CreateCubicBezierEasingFunction(InOutA, InOutB);

    private static void AnimateTranslation(Visual v, Vector3 to, TimeSpan d, Vector3? from = null)
    {
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        if (from is { } f) a.InsertKeyFrame(0f, f);
        a.InsertKeyFrame(1f, to, EaseOut(c));
        a.Duration = d;
        a.Target = "Translation";
        v.StartAnimation("Translation", a);
    }

    private static void AnimateScalar(Visual v, string prop, float to, TimeSpan d, float? from = null, CompositionEasingFunction? ease = null)
    {
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        if (from is { } f) a.InsertKeyFrame(0f, f);
        a.InsertKeyFrame(1f, to, ease ?? EaseOut(c));
        a.Duration = d;
        v.StartAnimation(prop, a);
    }

    // ---------------- page transitions ----------------

    /// <summary>New page: short fade with a gentle 10 px rise. With motion blur, a fading motion trail follows it.</summary>
    public static void PageEnter(FrameworkElement page, UIElement? trailHost)
    {
        var v = V(page);
        if (!Enabled)
        {
            v.StopAnimation("Opacity"); v.StopAnimation("Translation");
            v.Opacity = 1; v.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }
        AnimateScalar(v, "Opacity", 1f, Page, from: 0f, ease: EaseFade(v.Compositor));
        AnimateTranslation(v, Vector3.Zero, Page, from: new Vector3(0, 10, 0));
        if (MotionBlur && trailHost is not null) MotionTrail.Play(page, trailHost, new Vector3(0, 10, 0), Page);
    }

    /// <summary>
    /// Fades a still image of the outgoing content away (the page itself is already gone from the Frame), so pages
    /// cross-fade instead of cutting. <paramref name="done"/> runs when it has finished or was replaced.
    /// </summary>
    public static void FadeAway(UIElement snapshot, TimeSpan duration, Action done)
    {
        var v = V(snapshot);
        if (!Enabled) { v.Opacity = 0; done(); return; }
        var c = v.Compositor;
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        AnimateScalar(v, "Opacity", 0f, duration, from: 1f, ease: EaseFade(c));
        batch.End();
        batch.Completed += (_, _) => done();
    }

    // ---------------- notices ----------------

    public static void Appear(UIElement e, float fromY = 10)
    {
        if (!Enabled) return;
        var v = V(e);
        AnimateScalar(v, "Opacity", 1f, Dialog, from: 0f, ease: EaseFade(v.Compositor));
        AnimateTranslation(v, Vector3.Zero, Dialog, from: new Vector3(0, fromY, 0));
    }

    public static void Disappear(UIElement e, Action done)
    {
        if (!Enabled) { done(); return; }
        var v = V(e);
        var c = v.Compositor;
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        AnimateScalar(v, "Opacity", 0f, PageExit, ease: EaseFade(c));
        AnimateTranslation(v, new Vector3(0, 6, 0), PageExit);
        batch.End();
        batch.Completed += (_, _) => done();
    }

    // ---------------- micro-interactions ----------------

    /// <summary>A small, restrained scale "pop" (e.g. when a favorite star is toggled): up to 112 %, no bounce.</summary>
    public static void Pop(FrameworkElement e)
    {
        if (!Enabled) return;
        var v = V(e);
        v.CenterPoint = new Vector3((float)e.ActualWidth / 2, (float)e.ActualHeight / 2, 0);
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0.35f, new Vector3(1.12f, 1.12f, 1), EaseOut(c));
        a.InsertKeyFrame(1f, Vector3.One, EaseInOut(c));
        a.Duration = PopTime;
        v.StartAnimation("Scale", a);
    }

    // Attached: Motion.HoverLift="True" lifts an element 2 px on hover and presses it slightly on click.
    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(DependencyObject o) => (bool)o.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject o, bool v) => o.SetValue(HoverLiftProperty, v);

    private static readonly ConditionalWeakTable<UIElement, object> Wired = new();

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if (e.NewValue is not true) { ResetInteraction(fe); return; }
        if (Wired.TryGetValue(fe, out _)) return;
        Wired.Add(fe, new object());
        // handledEventsToo: Buttons mark pointer events handled; we still want the visual response.
        fe.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) => Lift(fe, -2)), true);
        fe.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => { Lift(fe, 0); Squeeze(fe, 1f); }), true);
        fe.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => { Lift(fe, 0); Squeeze(fe, 1f); }), true);
        fe.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => { Lift(fe, 0); Squeeze(fe, 1f); }), true);
        fe.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Squeeze(fe, 0.985f)), true);
        fe.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Squeeze(fe, 1f)), true);
        fe.Unloaded += (_, _) => ResetInteraction(fe);
        if (fe is Control control)
            control.IsEnabledChanged += (_, _) => { if (!control.IsEnabled) ResetInteraction(control); };
    }

    /// <summary>Release transient transforms immediately when motion preferences change.</summary>
    public static void ResetInteractions()
    {
        foreach (var entry in Wired)
            if (entry.Key is FrameworkElement { IsLoaded: true }) ResetInteraction(entry.Key);
    }

    private static void ResetInteraction(UIElement element)
    {
        var visual = V(element);
        visual.StopAnimation("Translation");
        visual.StopAnimation("Scale");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Scale = Vector3.One;
    }

    private static void Lift(FrameworkElement fe, float y)
    {
        if (!Enabled || !GetHoverLift(fe) || fe is Control { IsEnabled: false }) { ResetInteraction(fe); return; }
        AnimateTranslation(V(fe), new Vector3(0, y, 0), Hover);
    }

    private static void Squeeze(FrameworkElement fe, float s)
    {
        if (!Enabled || !GetHoverLift(fe) || fe is Control { IsEnabled: false }) { ResetInteraction(fe); return; }
        var v = V(fe);
        v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, new Vector3(s, s, 1), EaseOut(c));
        a.Duration = Press;
        v.StartAnimation("Scale", a);
    }

    /// <summary>Animates a window's root content in (used when switching between compact and full view).</summary>
    public static void WindowContentEnter(UIElement root)
    {
        if (!Enabled) return;
        var v = V(root);
        AnimateScalar(v, "Opacity", 1f, Page, from: 0f, ease: EaseFade(v.Compositor));
        AnimateTranslation(v, Vector3.Zero, Page, from: new Vector3(0, 8, 0));
    }
}
