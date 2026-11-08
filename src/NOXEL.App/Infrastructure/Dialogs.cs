// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Noxel.App.Infrastructure;

/// <summary>
/// One place that builds NOXEL's dialogs so they all share the glass background, 22 px corners, equal button
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
            Content = content is string s ? new TextBlock { Text = s, Style = (Style)res["BodyText"], Foreground = (Microsoft.UI.Xaml.Media.Brush)res.ThemeDictionariesLookup("NoxelTextSecondary"), MaxWidth = 420 } : content,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            // A ContentDialog paints its default button in the accent colour. Cancel should stay neutral, so when Cancel
            // is the safe choice it gets keyboard focus instead (Enter = Cancel, Escape = Cancel) without the accent.
            DefaultButton = primaryIsDefault && !destructive ? ContentDialogButton.Primary : ContentDialogButton.None,
            PrimaryButtonStyle = (Style)res[destructive ? "NoxelDangerButton" : primaryIsDefault ? "NoxelDialogAccentButton" : "NoxelDialogButton"],
            CloseButtonStyle = (Style)res["NoxelDialogButton"],
        };
        if (d.DefaultButton == ContentDialogButton.None)
            d.Opened += (_, _) => (FindByName(d, "CloseButton") as Control)?.Focus(FocusState.Programmatic);
        return d;
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
