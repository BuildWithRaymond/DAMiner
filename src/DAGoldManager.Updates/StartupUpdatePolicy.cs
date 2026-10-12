namespace DAGoldManager.Updates;

/// <summary>Pure startup rules: apply only a newer installed package and keep data outside the install.</summary>
public static class StartupUpdatePolicy
{
    public static bool ShouldApply(bool preview, bool ownsInstance, bool installed, string currentVersion, string? pendingVersion)
        => !preview && ownsInstance && installed && Version.TryParse(currentVersion, out var current) &&
           Version.TryParse(pendingVersion, out var pending) && pending > current;
    public static bool DataOutsideInstall(string installationDirectory, string dataDirectory)
    {
        string install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory));
        string data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        return !data.Equals(install, StringComparison.OrdinalIgnoreCase) &&
            !data.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
