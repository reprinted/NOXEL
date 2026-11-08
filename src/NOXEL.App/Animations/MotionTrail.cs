// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Noxel.App.Animations;

/// <summary>
/// Optional "motion blur" for page transitions, done entirely in the compositor.
///
/// What it is: two live copies of the entering page (a CompositionVisualSurface of the page's visual — no
/// screen capture, no bitmap copies) drawn on a separate layer, trailing behind the page's movement at low
/// opacity and fading out within the transition. The eye reads this as directional smear, like motion blur.
///
/// What it is not: a true per-pixel directional blur. Windows' composition effect set has no directional blur
/// and a Gaussian blur would need an extra effects library and a full-surface blur pass, so NOXEL uses the
/// trail instead. It exists only for the ~220 ms of a page transition and is torn down afterwards; it is
/// never applied while scrolling or to dialogs.
/// </summary>
internal static class MotionTrail
{
    public static void Play(FrameworkElement page, UIElement host, Vector3 travel, TimeSpan duration)
    {
        if (page.ActualWidth < 1 || page.ActualHeight < 1)
        {
            // First visit: not laid out yet. Run once it has a size.
            void OnSize(object s, SizeChangedEventArgs e)
            {
                page.SizeChanged -= OnSize;
                if (e.NewSize.Width >= 1) Play(page, host, travel, duration);
            }
            page.SizeChanged += OnSize;
            return;
        }

        var source = ElementCompositionPreview.GetElementVisual(page);
        var c = source.Compositor;
        var size = new Vector2((float)page.ActualWidth, (float)page.ActualHeight);

        var surface = c.CreateVisualSurface();
        surface.SourceVisual = source;
        surface.SourceSize = size;
        var brush = c.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.None;
        brush.HorizontalAlignmentRatio = 0;
        brush.VerticalAlignmentRatio = 0;

        var layer = c.CreateContainerVisual();
        layer.Size = size;
        ElementCompositionPreview.SetElementChildVisual(host, layer);

        var easing = c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        float[] lag = { 1.0f, 2.0f };
        float[] alpha = { 0.22f, 0.11f };
        for (int i = 0; i < lag.Length; i++)
        {
            var ghost = c.CreateSpriteVisual();
            ghost.Size = size;
            ghost.Brush = brush;
            ghost.Opacity = 0;
            layer.Children.InsertAtTop(ghost);

            var offset = c.CreateVector3KeyFrameAnimation();
            offset.InsertKeyFrame(0f, travel * (1 + lag[i]));
            offset.InsertKeyFrame(1f, Vector3.Zero, easing);
            offset.Duration = duration;
            ghost.StartAnimation("Offset", offset);

            var fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, alpha[i]);
            fade.InsertKeyFrame(1f, 0f, easing);
            fade.Duration = duration;
            ghost.StartAnimation("Opacity", fade);
        }
        batch.End();
        batch.Completed += (_, _) =>
        {
            ElementCompositionPreview.SetElementChildVisual(host, null);
            layer.Children.RemoveAll();
            layer.Dispose();
            brush.Dispose();
            surface.Dispose();
        };
    }
}
