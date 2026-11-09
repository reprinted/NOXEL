// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Noxel.App.Controls;

/// <summary>
/// A TextBlock with a very subtle drop shadow, used only for the wordmark, page headings, key values and the
/// active preset name. The shadow is a compositor DropShadow masked by the text's own alpha (TextBlock.GetAlphaMask),
/// drawn on a layer behind the text, so the text itself stays pixel-crisp. Dark theme: soft violet-black; light
/// theme: faint neutral. Disabled in high contrast.
/// </summary>
public sealed class ShadowText : Grid
{
    private readonly Grid _host = new() { IsHitTestVisible = false };
    private readonly TextBlock _text = new();
    private SpriteVisual? _sprite;
    private DropShadow? _shadow;
    private static readonly AccessibilitySettings Accessibility = new();

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ShadowText), new PropertyMetadata("", (d, e) => ((ShadowText)d).OnTextChanged((string)e.NewValue)));

    public static readonly DependencyProperty TextStyleProperty = DependencyProperty.Register(
        nameof(TextStyle), typeof(Style), typeof(ShadowText), new PropertyMetadata(null, (d, e) => ((ShadowText)d)._text.Style = (Style)e.NewValue));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Style? TextStyle { get => (Style?)GetValue(TextStyleProperty); set => SetValue(TextStyleProperty, value); }
    public TextBlock Inner => _text;

    public ShadowText()
    {
        Children.Add(_host);
        Children.Add(_text);
        _text.SizeChanged += (_, _) => UpdateShadow();
        ActualThemeChanged += (_, _) => UpdateShadow();
        Loaded += (_, _) => UpdateShadow();
        Unloaded += (_, _) => Release();
    }

    private void OnTextChanged(string text)
    {
        _text.Text = text ?? "";
        // The alpha mask follows the text, but refresh after layout so a same-size change is picked up too.
        DispatcherQueue?.TryEnqueue(UpdateShadow);
    }

    private void UpdateShadow()
    {
        if (!IsLoaded || _text.ActualWidth < 1) return;
        if (Accessibility.HighContrast) { Release(); return; }
        var compositor = ElementCompositionPreview.GetElementVisual(_host).Compositor;
        if (_sprite is null)
        {
            _shadow = compositor.CreateDropShadow();
            _sprite = compositor.CreateSpriteVisual();
            _sprite.Shadow = _shadow;
            ElementCompositionPreview.SetElementChildVisual(_host, _sprite);
        }
        _shadow!.Mask = _text.GetAlphaMask();
        _sprite.Size = new Vector2((float)_text.ActualWidth, (float)_text.ActualHeight);
        bool dark = ActualTheme != ElementTheme.Light;
        // One shadow for every ShadowText (the look of the title-bar wordmark): crisp dark drop, no colour, no halo.
        _shadow.Color = dark ? Color.FromArgb(255, 0x00, 0x00, 0x00) : Color.FromArgb(255, 0x1E, 0x18, 0x34);
        _shadow.Opacity = dark ? 0.9f : 0.30f;
        _shadow.BlurRadius = dark ? 2.5f : 3f;
        _shadow.Offset = new Vector3(0, 2f, 0);
    }

    private void Release()
    {
        if (_sprite is null) return;
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _sprite.Dispose();
        _shadow?.Dispose();
        _sprite = null;
        _shadow = null;
    }
}
