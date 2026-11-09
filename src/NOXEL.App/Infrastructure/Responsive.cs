// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Noxel.App.Infrastructure;

/// <summary>
/// Puts a page into the "Wide", "Medium" or "Narrow" visual state based on the width actually available to
/// the page (window width minus the navigation pane), not the window width. Pages declare the setters for each
/// state in a VisualStateGroup on their root element.
/// </summary>
public static class Responsive
{
    public const double WideMin = 1000;
    public const double MediumMin = 640;

    private static readonly ConditionalWeakTable<Control, StrongBox<string>> Current = new();

    public static void Attach(Control page)
    {
        page.SizeChanged += (_, e) => Apply(page, e.NewSize.Width);
        page.Loaded += (_, _) => Apply(page, page.ActualWidth, force: true);
    }

    public static string StateFor(double width) => width >= WideMin ? "Wide" : width >= MediumMin ? "Medium" : "Narrow";

    private static void Apply(Control page, double width, bool force = false)
    {
        if (width <= 0) return;
        var state = StateFor(width);
        var box = Current.GetValue(page, _ => new StrongBox<string>(""));
        if (!force && box.Value == state) return;
        box.Value = state;
        VisualStateManager.GoToState(page, state, false);
    }
}
