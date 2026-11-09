// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Xaml;

namespace Noxel.App.Infrastructure;

internal static class ResourceExtensions
{
    /// <summary>Looks up a theme-dependent NOXEL resource for the main window's current theme.</summary>
    public static object ThemeDictionariesLookup(this ResourceDictionary _, string key)
    {
        var app = Application.Current.Resources;
        var theme = App.Main?.Content is FrameworkElement fe && fe.ActualTheme == ElementTheme.Light ? "Light"
            : App.Main is null && App.Controller is not null && !Services.ThemeService.IsDark(App.Settings.Theme) ? "Light" : "Dark";
        foreach (var md in app.MergedDictionaries)
        {
            if (md.ThemeDictionaries.TryGetValue(theme, out var d) && d is ResourceDictionary rd && rd.TryGetValue(key, out var v)) return v;
        }
        return app[key];
    }
}
