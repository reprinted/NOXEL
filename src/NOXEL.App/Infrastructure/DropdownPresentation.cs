// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Noxel.App.Infrastructure;

/// <summary>Keep the native ComboBox keyboard behavior, with a menu anchored below its field.</summary>
public static class DropdownPresentation
{
    public static readonly DependencyProperty BelowFieldProperty = DependencyProperty.RegisterAttached(
        "BelowField", typeof(bool), typeof(DropdownPresentation), new PropertyMetadata(false, OnChanged));
    public static bool GetBelowField(DependencyObject value) => (bool)value.GetValue(BelowFieldProperty);
    public static void SetBelowField(DependencyObject value, bool enabled) => value.SetValue(BelowFieldProperty, enabled);

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ComboBox box) return;
        box.DropDownOpened -= OnOpened;
        if (args.NewValue is true) box.DropDownOpened += OnOpened;
    }

    private static void OnOpened(object? sender, object args)
    {
        if (sender is not ComboBox box) return;
        box.DispatcherQueue.TryEnqueue(() =>
        {
            if (!box.IsDropDownOpen || !box.IsLoaded || !GetBelowField(box)) return;
            if (FindPopup(box) is not { } popup) return;
            popup.PlacementTarget = box;
            popup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedLeft;
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 2;
            if (popup.Child is FrameworkElement menu) menu.Width = box.ActualWidth;
            if (popup.Child is Border border) border.CornerRadius = new CornerRadius(6);
        });
    }

    private static Popup? FindPopup(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Popup popup) return popup;
            if (FindPopup(child) is { } found) return found;
        }
        return null;
    }
}
