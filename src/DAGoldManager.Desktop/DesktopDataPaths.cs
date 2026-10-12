namespace DAGoldManager.Desktop;

internal static class DesktopDataPaths
{
    public static string Resolve(bool demo) => Environment.GetEnvironmentVariable("DAGOLDMANAGER_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            demo ? "DAGoldManagerPreview" : "DAGoldManager", "Data");
}
