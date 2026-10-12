using DAGoldManager.Updates;
using Velopack;

namespace DAGoldManager.Desktop;

internal static class UpdateBootstrap
{
    public static string? Error { get; private set; }

    public static void ApplyPendingAtStartup(string[] arguments)
    {
        try
        {
            if (!StartupUpdatePolicy.DataOutsideInstall(AppContext.BaseDirectory, DesktopDataPaths.Resolve(false)))
            {
                Error = "Updates paused: saved data must be outside the application folder.";
                return;
            }
            var manager = new UpdateManager(AppContext.BaseDirectory);
            var pending = manager.IsInstalled ? manager.UpdatePendingRestart : null;
            if (StartupUpdatePolicy.ShouldApply(false, true, manager.IsInstalled,
                manager.CurrentVersion?.ToNormalizedString() ?? UpdateBuildInfo.Version, pending?.Version.ToNormalizedString()))
                manager.ApplyUpdatesAndRestart(pending, arguments);
        }
        catch (Exception error)
        {
            // Continue with the current app if its staged update cannot be applied.
            Error = $"Update installation failed: {error.Message}";
        }
    }
}
