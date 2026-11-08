using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
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
    public static readonly TimeSpan Hover = TimeSpan.FromMilliseconds(140);
    public static readonly TimeSpan Press = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Toggle = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Dropdown = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan Dialog = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Page = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan Sidebar = TimeSpan.FromMilliseconds(240);

    /// <summary>Ease-out used for entrances and hover; ease-in-out for value changes.</summary>
    private static readonly Vector2 OutA = new(0.1f, 0.9f), OutB = new(0.2f, 1f);
    private static readonly Vector2 InOutA = new(0.65f, 0f), InOutB = new(0.35f, 1f);

    public static bool Enabled => App.Controller is not null && ThemeService.AnimationsAllowed(App.Settings);
    public static bool MotionBlur => Enabled && App.Settings.MotionBlur;

    private static Visual V(UIElement e)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        return ElementCompositionPreview.GetElementVisual(e);
    }

    private static CompositionEasingFunction EaseOut(Compositor c) => c.CreateCubicBezierEasingFunction(OutA, OutB);
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

    /// <summary>New page: short fade with a gentle 14 px rise. With motion blur, a fading motion trail follows it.</summary>
    public static void PageEnter(FrameworkElement page, UIElement? trailHost)
    {
        var v = V(page);
        if (!Enabled)
        {
            v.StopAnimation("Opacity"); v.StopAnimation("Translation");
            v.Opacity = 1; v.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }
        AnimateScalar(v, "Opacity", 1f, Page, from: 0f);
        AnimateTranslation(v, Vector3.Zero, Page, from: new Vector3(0, 14, 0));
        if (MotionBlur && trailHost is not null) MotionTrail.Play(page, trailHost, new Vector3(0, 14, 0), Page);
    }

    // ---------------- notices ----------------

    public static void Appear(UIElement e, float fromY = 10)
    {
        if (!Enabled) return;
        var v = V(e);
        AnimateScalar(v, "Opacity", 1f, Dialog, from: 0f);
        AnimateTranslation(v, Vector3.Zero, Dialog, from: new Vector3(0, fromY, 0));
    }

    public static void Disappear(UIElement e, Action done)
    {
        if (!Enabled) { done(); return; }
        var v = V(e);
        var c = v.Compositor;
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        AnimateScalar(v, "Opacity", 0f, Dialog);
        AnimateTranslation(v, new Vector3(0, 8, 0), Dialog);
        batch.End();
        batch.Completed += (_, _) => done();
    }

    // ---------------- micro-interactions ----------------

    /// <summary>A small scale "pop" (e.g. when a favorite star is toggled).</summary>
    public static void Pop(FrameworkElement e)
    {
        if (!Enabled) return;
        var v = V(e);
        v.CenterPoint = new Vector3((float)e.ActualWidth / 2, (float)e.ActualHeight / 2, 0);
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0.4f, new Vector3(1.25f, 1.25f, 1), EaseOut(c));
        a.InsertKeyFrame(1f, Vector3.One, EaseInOut(c));
        a.Duration = TimeSpan.FromMilliseconds(260);
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
        if (d is not FrameworkElement fe || e.NewValue is not true || Wired.TryGetValue(fe, out _)) return;
        Wired.Add(fe, new object());
        // handledEventsToo: Buttons mark pointer events handled; we still want the visual response.
        fe.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) => Lift(fe, -2)), true);
        fe.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => { Lift(fe, 0); Squeeze(fe, 1f); }), true);
        fe.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => { Lift(fe, 0); Squeeze(fe, 1f); }), true);
        fe.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Squeeze(fe, 0.985f)), true);
        fe.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Squeeze(fe, 1f)), true);
    }

    private static void Lift(FrameworkElement fe, float y)
    {
        if (!Enabled) return;
        AnimateTranslation(V(fe), new Vector3(0, y, 0), Hover);
    }

    private static void Squeeze(FrameworkElement fe, float s)
    {
        if (!Enabled) return;
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
        AnimateScalar(v, "Opacity", 1f, Page, from: 0f);
        AnimateTranslation(v, Vector3.Zero, Page, from: new Vector3(0, 8, 0));
    }
}
