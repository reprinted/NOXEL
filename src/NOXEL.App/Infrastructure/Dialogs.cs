// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Noxel.App.Infrastructure;

/// <summary>
/// One place that builds NOXEL's dialogs so they all share the glass background, 24 px corners, equal button
/// heights/padding/typography and safe keyboard defaults. Destructive actions use the rounded red
/// "NoxelDangerButton" style and Cancel is always the default (Enter) and Escape action.
/// The display Keep/Revert prompt is a separate always-on-top window and is deliberately not built here.
/// </summary>
public static class Dialogs
{
    public static ContentDialog Create(XamlRoot root, ElementTheme theme, string title, object content,
        string primaryText, string closeText = "Cancel", bool destructive = false, bool primaryIsDefault = false)
    {
        var res = Application.Current.Resources;
        var d = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = title,
            Content = content is string s ? new TextBlock { Text = s, Style = (Style)res["DialogBodyText"], MaxWidth = 420 } : content,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            // A ContentDialog paints its default button in the accent colour. Cancel should stay neutral, so when Cancel
            // is the safe choice it gets keyboard focus instead (Enter = Cancel, Escape = Cancel) without the accent.
            DefaultButton = primaryIsDefault && !destructive ? ContentDialogButton.Primary : ContentDialogButton.None,
            PrimaryButtonStyle = (Style)res[destructive ? "NoxelDangerButton" : primaryIsDefault ? "NoxelDialogAccentButton" : "NoxelDialogButton"],
            CloseButtonStyle = (Style)res["NoxelDialogButton"],
        };
        // WinUI's default-button state replaces the Style with its built-in AccentButtonStyle.
        // Apply our existing template locally once the button exists; retain the native default-button semantics.
        if (d.DefaultButton == ContentDialogButton.Primary)
            WhenPartReady(d, "PrimaryButton", button =>
            {
                var style = (Style)res["NoxelPrimaryButton"];
                var template = style.Setters.OfType<Setter>().First(s => s.Property == Control.TemplateProperty);
                button.Template = (ControlTemplate)template.Value;
                button.CornerRadius = (CornerRadius)res["NoxelRadiusPill"];
                button.Padding = new Thickness(18, 9, 18, 9);
                button.MinHeight = 44;
                // The stock accent style also resets these; keep them equal to NoxelPrimaryButton / the Cancel button.
                button.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                button.BorderThickness = new Thickness(1);
                button.HorizontalContentAlignment = HorizontalAlignment.Center;
                button.VerticalContentAlignment = VerticalAlignment.Center;
                button.FocusVisualMargin = new Thickness(-3);
                button.UseSystemFocusVisuals = true;
                Animations.Motion.SetHoverLift(button, true);
            });
        if (d.DefaultButton == ContentDialogButton.None)
        {
            WhenPartReady(d, "CloseButton", button => button.Focus(FocusState.Programmatic));
            // Focus that nobody asked for (the dialog's own initial focus, or focus restored when the window becomes
            // active again) must never land on the action button, or a stray Enter would delete. Send it to Cancel.
            // Focus moved by the user (Tab, arrow keys, click, touch) is left alone.
            d.GettingFocus += (_, a) =>
            {
                if (a.FocusState == FocusState.Programmatic && a.NewFocusedElement is Button { Name: "PrimaryButton" }
                    && FindByName(d, "CloseButton") is Button close)
                    a.TrySetNewFocusedElement(close);
            };
        }
        return d;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a dialog button as soon as it exists. At Opened the dialog's template is not
    /// always applied yet (for example when it opens while NOXEL is not the active window), so a lookup made only
    /// there can find nothing and silently skip the styling or the safe "focus Cancel" default.
    /// </summary>
    private static void WhenPartReady(ContentDialog d, string name, Action<Button> action)
    {
        bool done = false;
        void OnLayout(object? sender, object e) => Try();
        void Try()
        {
            if (done || FindByName(d, name) is not Button button) return;
            done = true;
            d.LayoutUpdated -= OnLayout;
            action(button);
        }
        d.Opened += (_, _) => { Try(); if (!done) d.LayoutUpdated += OnLayout; };
        d.Closed += (_, _) => d.LayoutUpdated -= OnLayout;
    }

    private static ContentDialog? _open;

    /// <summary>
    /// Shows the dialog unless another one is already open (WinUI allows only one, and a second ShowAsync from an
    /// async handler ends the process without reaching the crash handler). Returns None when it wasn't shown.
    /// </summary>
    public static async Task<ContentDialogResult> ShowSafeAsync(this ContentDialog d)
    {
        if (_open is not null) return ContentDialogResult.None;
        _open = d;
        try { return await d.ShowAsync(); }
        catch (Exception ex)
        {
            Noxel.Core.Infrastructure.Log.Warn("Dialog failed: " + ex.Message);
            return ContentDialogResult.None;
        }
        finally { _open = null; }
    }

    private static DependencyObject? FindByName(DependencyObject parent, string name)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name) return child;
            if (FindByName(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>Delete-style confirmation. Returns true only when the destructive button was chosen.</summary>
    public static async Task<bool> ConfirmDestructiveAsync(XamlRoot root, ElementTheme theme, string title, string message, string action = "Delete")
    {
        var d = Create(root, theme, title, message, action, "Cancel", destructive: true);
        return await d.ShowSafeAsync() == ContentDialogResult.Primary;
    }
}
