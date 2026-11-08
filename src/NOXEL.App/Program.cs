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
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Crash("Unhandled exception", ex);
            App.EmergencyRestore();
        };

        // Second launch: hand focus to the running instance instead of starting another tray process.
        Instance = new SingleInstance();
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
