// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Noxel.App.Services;

/// <summary>
/// "Translucent" window material: a Desktop Acrylic backdrop with no tint of its own, so whatever is behind NOXEL
/// (desktop, wallpaper, other windows) shows through blurred in its real colours. NOXEL's own window tint layer and
/// glass cards sit on top and set how see-through it looks (Glass intensity). The blur stays when NOXEL loses focus.
/// Windows falls back to a solid colour by itself when Transparency effects or battery saver turn blur off.
/// </summary>
public sealed class TranslucentBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _config;
    public bool Dark { get; }

    public TranslucentBackdrop(bool dark) => Dark = dark;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _config = new SystemBackdropConfiguration
        {
            IsInputActive = true, // keep the blur while another window has focus
            Theme = Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light,
        };
        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Base,
            TintColor = Dark ? Color.FromArgb(255, 0x0A, 0x0B, 0x16) : Color.FromArgb(255, 0xF5, 0xF3, 0xFC),
            TintOpacity = 0f,
            LuminosityOpacity = Dark ? 0.12f : 0.18f,
            FallbackColor = Dark ? Color.FromArgb(255, 0x0E, 0x0E, 0x1A) : Color.FromArgb(255, 0xF1, 0xEF, 0xF8),
        };
        _controller.AddSystemBackdropTarget(target);
        _controller.SetSystemBackdropConfiguration(_config);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
        _config = null;
    }
}
