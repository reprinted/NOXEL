// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Noxel.Core.Hotkeys;
using Noxel.Core.Infrastructure;
using static Noxel.Windows.NativeInterop.Win32;

namespace Noxel.Windows.SystemIntegration;

public enum SystemSignal { DisplayChanged, DevicesChanged, SessionUnlocked, SessionLocked, Resumed }

/// <param name="AccessKey">When true, an '&amp;' in <paramref name="Text"/> marks the keyboard access key. User text (preset names) keeps this false so '&amp;' is shown literally.</param>
public sealed record TrayMenuItem(int Id, string Text, bool Enabled = true, bool Checked = false, bool IsSeparator = false, bool IsDefault = false, IReadOnlyList<TrayMenuItem>? Children = null, bool AccessKey = false)
{
    public static TrayMenuItem Separator { get; } = new(0, "", IsSeparator: true);
}

/// <summary>
/// A dedicated STA thread owning a hidden top-level window. It receives WM_HOTKEY, tray icon callbacks,
/// WM_DISPLAYCHANGE / WM_DEVICECHANGE broadcasts, session lock/unlock and power resume notifications, and it
/// hosts the Magnification API (which must be used from a single thread). Everything is event driven:
/// the thread sleeps in GetMessage and uses no CPU while idle.
/// Events are raised ON THE HOST THREAD; the app marshals them to its UI thread.
/// </summary>
public sealed unsafe class NativeHost : IDisposable
{
    private const uint WM_INVOKE = WM_APP + 1;
    private const uint WM_TRAY = WM_APP + 2;
    private const uint TrayId = 1;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private IntPtr _hwnd;
    private uint _taskbarCreated;
    private static NativeHost? _instance;
    private static readonly delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> WndProcPtr = &StaticWndProc;

    private IntPtr _trayIcon;
    private string _trayTooltip = "NOXEL";
    private bool _trayVisible;
    private readonly Dictionary<int, HotkeyGesture> _hotkeys = new();

    public event Action<int>? HotkeyPressed;
    public event Action<SystemSignal>? Signal;
    public event Action? TrayActivated;
    public event Action<int>? TrayCommand;
    /// <summary>Builds the tray menu when it opens. Called on the host thread; must be thread-safe.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? TrayMenuProvider { get; set; }

    public IntPtr Hwnd => _hwnd;

    public NativeHost()
    {
        _instance = this;
        _thread = new Thread(Run) { IsBackground = true, Name = "NOXEL native host" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("Native host failed to start.");
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("Native host window could not be created.");
    }

    private void Run()
    {
        try
        {
            var cls = "NOXEL.NativeHost";
            fixed (char* pCls = cls)
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)sizeof(WNDCLASSEX),
                    lpfnWndProc = (IntPtr)WndProcPtr,
                    hInstance = GetModuleHandleW(null),
                    lpszClassName = pCls,
                };
                RegisterClassExW(ref wc);
            }
            // Hidden top-level popup (not message-only) so it receives broadcast messages like WM_DISPLAYCHANGE.
            _hwnd = CreateWindowExW(0x80 /*WS_EX_TOOLWINDOW*/, cls, "NOXEL host", 0x80000000 /*WS_POPUP*/, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            if (_hwnd != IntPtr.Zero) WTSRegisterSessionNotification(_hwnd, 0);
        }
        catch (Exception ex) { Log.Error("Native host init", ex); }
        finally { _ready.Set(); }

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var self = _instance;
            if (self is not null && self.WndProc(hwnd, msg, wParam, lParam, out var result)) return result;
        }
        catch (Exception ex) { Log.Error($"WndProc {msg:X}", ex); }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private bool WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        switch (msg)
        {
            case WM_INVOKE:
                while (_queue.TryDequeue(out var a))
                {
                    try { a(); } catch (Exception ex) { Log.Error("Native host invoke", ex); }
                }
                return true;
            case WM_HOTKEY:
                HotkeyPressed?.Invoke((int)wParam);
                return true;
            case WM_DISPLAYCHANGE:
                Signal?.Invoke(SystemSignal.DisplayChanged);
                return false;
            case WM_DEVICECHANGE when (int)wParam == DBT_DEVNODES_CHANGED:
                Signal?.Invoke(SystemSignal.DevicesChanged);
                return false;
            case WM_WTSSESSION_CHANGE:
                if ((int)wParam == WTS_SESSION_UNLOCK) Signal?.Invoke(SystemSignal.SessionUnlocked);
                else if ((int)wParam == WTS_SESSION_LOCK) Signal?.Invoke(SystemSignal.SessionLocked);
                return false;
            case WM_POWERBROADCAST when (int)wParam is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND:
                Signal?.Invoke(SystemSignal.Resumed);
                result = (IntPtr)1;
                return true;
            case WM_TRAY:
                HandleTray((uint)(lParam.ToInt64() & 0xFFFF));
                return true;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0 && _trayVisible)
        {
            // Explorer restarted: re-add our icon.
            _trayVisible = false;
            AddOrUpdateTray();
            return true;
        }
        return false;
    }

    // ---------------- Invoke ----------------

    public void Post(Action a)
    {
        _queue.Enqueue(a);
        PostMessageW(_hwnd, WM_INVOKE, IntPtr.Zero, IntPtr.Zero);
    }

    public T Invoke<T>(Func<T> f)
    {
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId) return f();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { tcs.SetResult(f()); } catch (Exception ex) { tcs.SetException(ex); }
        });
        if (!tcs.Task.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Native host did not respond.");
        return tcs.Task.Result;
    }

    public void Invoke(Action a) => Invoke(() => { a(); return true; });

    // ---------------- Hotkeys ----------------

    /// <summary>Registers a global hotkey. Returns null on success or a user-facing error.</summary>
    public string? RegisterHotkey(int id, HotkeyGesture g) => Invoke(() =>
    {
        if (_hotkeys.ContainsKey(id)) { UnregisterHotKey(_hwnd, id); _hotkeys.Remove(id); }
        uint mods = MOD_NOREPEAT;
        if (g.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= MOD_ALT;
        if (g.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods |= MOD_CONTROL;
        if (g.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= MOD_SHIFT;
        if (g.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= MOD_WIN;
        if (!RegisterHotKey(_hwnd, id, mods, g.VirtualKey))
        {
            int err = Marshal.GetLastPInvokeError();
            return err == 1409 ? $"{g} is already used by another app." : $"Windows couldn't register {g} (error {err}).";
        }
        _hotkeys[id] = g;
        return (string?)null;
    });

    public void UnregisterAllHotkeys() => Invoke(() =>
    {
        foreach (var id in _hotkeys.Keys) UnregisterHotKey(_hwnd, id);
        _hotkeys.Clear();
    });

    // ---------------- Tray ----------------

    public void ShowTray(string iconPath, string tooltip) => Invoke(() =>
    {
        if (_trayIcon == IntPtr.Zero)
        {
            uint dpi = GetDpiForSystem();
            int cx = GetSystemMetricsForDpi(SM_CXSMICON, dpi), cy = GetSystemMetricsForDpi(SM_CYSMICON, dpi);
            _trayIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
        }
        _trayTooltip = tooltip;
        AddOrUpdateTray();
    });

    public void SetTrayTooltip(string tooltip) => Post(() =>
    {
        _trayTooltip = tooltip;
        if (_trayVisible) AddOrUpdateTray();
    });

    public void ShowBalloon(string title, string text) => Post(() =>
    {
        if (!_trayVisible) return;
        var d = NewData();
        d.uFlags = NIF_INFO;
        d.szInfoTitle = Trunc(title, 63);
        d.szInfo = Trunc(text, 255);
        d.dwInfoFlags = NIIF_INFO | NIIF_NOSOUND;
        Shell_NotifyIconW(NIM_MODIFY, ref d);
    });

    public void HideTray() => Invoke(() =>
    {
        if (!_trayVisible) return;
        var d = NewData();
        Shell_NotifyIconW(NIM_DELETE, ref d);
        _trayVisible = false;
    });

    private void AddOrUpdateTray()
    {
        var d = NewData();
        d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        d.uCallbackMessage = WM_TRAY;
        d.hIcon = _trayIcon;
        d.szTip = Trunc(_trayTooltip, 127);
        if (_trayVisible) Shell_NotifyIconW(NIM_MODIFY, ref d);
        else _trayVisible = Shell_NotifyIconW(NIM_ADD, ref d);
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = TrayId,
        szTip = "", szInfo = "", szInfoTitle = "",
    };

    private void HandleTray(uint evt)
    {
        switch (evt)
        {
            case WM_LBUTTONUP:
            case WM_LBUTTONDBLCLK:
                TrayActivated?.Invoke();
                break;
            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                ShowTrayMenu();
                break;
        }
    }

    private void ShowTrayMenu()
    {
        var items = TrayMenuProvider?.Invoke();
        if (items is null || items.Count == 0) return;
        var menu = Build(items);
        try
        {
            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd); // required so the menu closes when clicking elsewhere
            int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN | TPM_RIGHTALIGN, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (cmd != 0) TrayCommand?.Invoke(cmd);
        }
        finally { DestroyMenu(menu); }
    }

    private static IntPtr Build(IReadOnlyList<TrayMenuItem> items)
    {
        var menu = CreatePopupMenu();
        foreach (var it in items)
        {
            if (it.IsSeparator) { AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null); continue; }
            uint flags = MF_STRING | (it.Enabled ? 0 : MF_GRAYED) | (it.Checked ? MF_CHECKED : 0) | (it.IsDefault ? MF_DEFAULT : 0);
            if (it.Children is { Count: > 0 })
            {
                var sub = Build(it.Children); // destroyed together with the parent menu
                AppendMenuW(menu, flags | MF_POPUP, (UIntPtr)(ulong)sub, it.AccessKey ? it.Text : Escape(it.Text));
            }
            else AppendMenuW(menu, flags, (UIntPtr)(uint)it.Id, it.AccessKey ? it.Text : Escape(it.Text));
        }
        return menu;
    }

    private static string Escape(string s) => s.Replace("&", "&&");
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];

    public void Dispose()
    {
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                Invoke(() =>
                {
                    foreach (var id in _hotkeys.Keys) UnregisterHotKey(_hwnd, id);
                    _hotkeys.Clear();
                    if (_trayVisible) { var d = NewData(); Shell_NotifyIconW(NIM_DELETE, ref d); _trayVisible = false; }
                    if (_trayIcon != IntPtr.Zero) { DestroyIcon(_trayIcon); _trayIcon = IntPtr.Zero; }
                    WTSUnRegisterSessionNotification(_hwnd);
                    DestroyWindow(_hwnd);
                    PostQuitMessage(0);
                });
            }
        }
        catch (Exception ex) { Log.Error("Native host dispose", ex); }
        _hwnd = IntPtr.Zero;
    }
}
