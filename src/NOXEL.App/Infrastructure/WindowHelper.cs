using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noxel.Core.Settings;
using Windows.Graphics;

namespace Noxel.App.Infrastructure;

public static class WindowHelper
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int pid);

    public static IntPtr Hwnd(Window w) => WinRT.Interop.WindowNative.GetWindowHandle(w);

    public static double Scale(Window w)
    {
        uint dpi = GetDpiForWindow(Hwnd(w));
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "noxel.ico");

    public static void SetIcon(Window w)
    {
        try { w.AppWindow.SetIcon(IconPath); } catch { /* icon is cosmetic */ }
    }

    public static void SetMinSize(Window w, double minWDip, double minHDip)
    {
        if (w.AppWindow.Presenter is OverlappedPresenter p)
        {
            double s = Scale(w);
            p.PreferredMinimumWidth = (int)(minWDip * s);
            p.PreferredMinimumHeight = (int)(minHDip * s);
        }
    }

    /// <summary>Restores a saved position if it is still on a connected monitor; otherwise centers on the primary display.</summary>
    public static void RestorePlacement(Window w, WindowPlacement p, double defWDip, double defHDip, bool resize = true)
    {
        double s = Scale(w);
        int width = resize ? (p.Width > 0 ? p.Width : (int)(defWDip * s)) : w.AppWindow.Size.Width;
        int height = resize ? (p.Height > 0 ? p.Height : (int)(defHDip * s)) : w.AppWindow.Size.Height;
        if (p.HasPosition)
        {
            var rect = new RectInt32(p.X, p.Y, width, height);
            var area = DisplayArea.GetFromRect(rect, DisplayAreaFallback.None);
            if (area is not null)
            {
                // Keep the title bar reachable.
                var wa = area.WorkArea;
                int x = Math.Clamp(p.X, wa.X - width + 120, wa.X + wa.Width - 120);
                int y = Math.Clamp(p.Y, wa.Y, wa.Y + wa.Height - 60);
                w.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
                return;
            }
        }
        var primary = DisplayArea.Primary.WorkArea;
        width = Math.Min(width, primary.Width);
        height = Math.Min(height, primary.Height);
        w.AppWindow.MoveAndResize(new RectInt32(primary.X + (primary.Width - width) / 2, primary.Y + (primary.Height - height) / 2, width, height));
    }

    public static void SavePlacement(Window w, WindowPlacement p)
    {
        if (w.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) return;
        p.Maximized = w.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (p.Maximized) return;
        p.X = w.AppWindow.Position.X;
        p.Y = w.AppWindow.Position.Y;
        p.Width = w.AppWindow.Size.Width;
        p.Height = w.AppWindow.Size.Height;
    }

    public static void CenterOnPoint(Window w, int x, int y)
    {
        var area = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Primary);
        var wa = area.WorkArea;
        var size = w.AppWindow.Size;
        w.AppWindow.Move(new PointInt32(wa.X + (wa.Width - size.Width) / 2, wa.Y + (wa.Height - size.Height) / 2));
    }

    public static void BringToFront(Window w)
    {
        if (!w.AppWindow.IsVisible) w.AppWindow.Show();
        if (w.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } op) op.Restore();
        w.Activate();
        SetForegroundWindow(Hwnd(w));
    }

    /// <summary>Lets the already-running instance take focus when a second launch signals it.</summary>
    public static void AllowAnyForeground() => AllowSetForegroundWindow(-1);
}
