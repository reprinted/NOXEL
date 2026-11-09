// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Noxel.Core.Infrastructure;
using Noxel.Windows.SystemIntegration;

namespace Noxel.App;

public static class Program
{
    internal static SingleInstance? Instance { get; private set; }
    internal static string[] Args { get; private set; } = Array.Empty<string>();

    [STAThread]
    private static int Main(string[] args)
    {
        Args = args;
        // Testing only: keep a throwaway copy's settings, presets and downloads apart from the real ones.
        var testScope = "";
        if (Environment.GetEnvironmentVariable("NOXEL_TEST_DATA_DIR") is { Length: > 0 } testData && Path.IsPathFullyQualified(testData))
        {
            AppPaths.Root = testData;
            testScope = ".test";
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Crash("Unhandled exception", ex);
            App.EmergencyRestore();
        };

        // Second launch: hand focus to the running instance instead of starting another tray process.
        // Portable update helper mode: this is the new, verified NOXEL.exe replacing the old one. No UI, no instance lock.
        if (args.Any(a => a.Equals("--apply-portable-update", StringComparison.OrdinalIgnoreCase)))
            return Services.PortableUpdater.Run(args);

        Instance = new SingleInstance(testScope);
        if (!Instance.IsFirst)
        {
            Infrastructure.WindowHelper.AllowAnyForeground();
            Instance.SignalFirstInstance();
            Instance.Dispose();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(ctx);
            _ = new App();
        });
        Instance.Dispose();
        return 0;
    }
}
