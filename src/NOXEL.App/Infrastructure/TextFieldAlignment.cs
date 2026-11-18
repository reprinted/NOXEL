// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Noxel.App.Infrastructure;

/// <summary>
/// Makes single-line TextBoxes honour <see cref="Control.VerticalContentAlignment"/>.
///
/// Root cause: WinUI's default TextBox template never binds VerticalContentAlignment. Its text area
/// ("ContentElement") and placeholder ("PlaceholderTextContentPresenter") stretch to the full field height and draw
/// from the top padding down, so in NOXEL's 44 px fields the text sat a couple of pixels high, while ComboBox (whose
/// template does honour it) was centred. Applied once from the shared TextBox style, this aligns both template parts
/// the same way, so typed text and placeholder share one baseline at any height, DPI or font size.
/// Boxes that grow with their text (multi-line, auto height) look the same either way; a fixed-height multi-line
/// box that wants its text at the top sets VerticalContentAlignment="Top".
/// </summary>
public static class TextFieldAlignment
{
    public static readonly DependencyProperty HonorVerticalContentAlignmentProperty = DependencyProperty.RegisterAttached(
        "HonorVerticalContentAlignment", typeof(bool), typeof(TextFieldAlignment), new PropertyMetadata(false, OnChanged));

    public static bool GetHonorVerticalContentAlignment(DependencyObject o) => (bool)o.GetValue(HonorVerticalContentAlignmentProperty);
    public static void SetHonorVerticalContentAlignment(DependencyObject o, bool v) => o.SetValue(HonorVerticalContentAlignmentProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.Loaded -= OnLoaded;
        if (e.NewValue is true)
        {
            box.Loaded += OnLoaded;
            if (box.IsLoaded) Apply(box);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Apply((TextBox)sender);

    private static void Apply(TextBox box)
    {
        if (VisualTreeHelper.GetChildrenCount(box) == 0) return; // template not applied yet; Loaded comes after it is
        var align = box.VerticalContentAlignment;
        foreach (var name in new[] { "ContentElement", "PlaceholderTextContentPresenter" })
            if (Find(box, name) is FrameworkElement part) part.VerticalAlignment = align;
    }

    private static FrameworkElement? Find(DependencyObject parent, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            if (Find(child, name) is { } found) return found;
        }
        return null;
    }
}
