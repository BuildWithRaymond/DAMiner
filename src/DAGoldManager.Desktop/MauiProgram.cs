using DAGoldManager.UI;
using DAGoldManager.Updates;
using System.Reflection;

namespace DAGoldManager.Desktop;

/// <summary>Composes shared UI, the live-or-demo dashboard, and the independent updater controller.</summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
        bool demo = Environment.GetCommandLineArgs().Contains("--demo");
        // Preserve the original data location and override after the DAMiner
        // rebrand so saved accounts and Windows-encrypted passwords still load.
        string dataDirectory = DesktopDataPaths.Resolve(demo);
        builder.Services.AddSingleton(new DashboardModel(demo, dataDirectory, Path.Combine(AppContext.BaseDirectory, "content")));
        string repository = typeof(MauiProgram).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? "";
        string updateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DAMiner", "Updates");
        var updateClient = demo ? null : new VelopackUpdateClient(UpdateBootstrap.Error);
        builder.Services.AddSingleton(new ApplicationUpdates(updateClient, updateDirectory,
            updateClient?.InstalledVersion ?? UpdateBuildInfo.Version, repository));
        return builder.Build();
    }
}
