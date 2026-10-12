using DAGoldManager.Updates;
using Velopack;

namespace DAGoldManager.Desktop.WinUI;

internal static class DesktopProgram
{
    [STAThread]
    static void Main(string[] args)
    {
        bool preview = args.Contains("--demo");
        // Fast installation hooks must finish before any WinUI or game-data startup.
        if (!preview) VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var instance = preview ? null : InstallationInstance.TryAcquire(AppContext.BaseDirectory);
        if (!preview && instance is null) return;
        if (!preview) UpdateBootstrap.ApplyPendingAtStartup(args);
        StartWindow();
    }

    static void StartWindow()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(dispatcher));
            new App();
        });
    }
}
