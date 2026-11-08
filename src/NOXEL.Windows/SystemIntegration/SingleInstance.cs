// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

namespace Noxel.Windows.SystemIntegration;

/// <summary>
/// One NOXEL per user session. A second launch signals the first (which brings its window forward) and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\NOXEL.SingleInstance.7f3c2a91";
    private const string EventName = @"Local\NOXEL.Activate.7f3c2a91";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activate;
    private RegisteredWaitHandle? _wait;

    public bool IsFirst { get; }

    /// <param name="scope">Empty for normal use. Isolated test copies (NOXEL_TEST_DATA_DIR) pass a scope so they can
    /// run next to a real NOXEL; the installer only waits for the unscoped name.</param>
    public SingleInstance(string scope = "")
    {
        _mutex = new Mutex(true, MutexName + scope, out bool created);
        IsFirst = created;
        if (!created)
        {
            try { _mutex.WaitOne(0); } catch (AbandonedMutexException) { IsFirst = true; } // previous instance crashed
        }
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName + scope);
    }

    /// <summary>Called by a second instance.</summary>
    public void SignalFirstInstance() => _activate?.Set();

    /// <summary>Called by the first instance; the callback runs on a thread-pool thread.</summary>
    public void ListenForActivation(Action onActivate)
    {
        if (_activate is null) return;
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate?.Dispose();
        if (IsFirst) { try { _mutex.ReleaseMutex(); } catch { } }
        _mutex.Dispose();
    }
}
