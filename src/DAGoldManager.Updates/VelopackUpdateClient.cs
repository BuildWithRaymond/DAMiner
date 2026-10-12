using Velopack;
using Velopack.Sources;

namespace DAGoldManager.Updates;

// This adapter can check and download, but cannot restart or install an update.
public sealed class VelopackUpdateClient : IUpdateClient
{
    readonly UpdateManager installed;
    readonly Func<string, UpdateManager> createSource;
    UpdateManager? downloadManager;
    UpdateInfo? offered;

    public VelopackUpdateClient(string? unavailableReason = null)
        : this(new UpdateManager(AppContext.BaseDirectory), repository =>
            new UpdateManager(new GithubSource($"https://github.com/{repository}", null, prerelease: false)), unavailableReason) { }

    public VelopackUpdateClient(UpdateManager installed, Func<string, UpdateManager> createSource, string? unavailableReason = null)
    {
        this.installed = installed;
        this.createSource = createSource;
        UnavailableReason = unavailableReason;
    }

    public string? UnavailableReason { get; }
    public bool IsInstalled => UnavailableReason is null && installed.IsInstalled;
    public string? InstalledVersion => installed.IsInstalled ? installed.CurrentVersion?.ToNormalizedString() : null;
    public string? PendingVersion => IsInstalled ? installed.UpdatePendingRestart?.Version.ToNormalizedString() : null;

    public async Task<string?> CheckAsync(string repository, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        downloadManager = createSource(GitHubRepository.Normalize(repository));
        // Bound release discovery so an unreachable feed does not leave Settings stuck checking.
        offered = await downloadManager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(45), cancellation);
        if (offered is null) return null;
        UpdatePackageValidation.Validate(offered.TargetFullRelease);
        return offered.TargetFullRelease.Version.ToNormalizedString();
    }

    public async Task DownloadAsync(string version, Action<int> progress, CancellationToken cancellation)
    {
        if (downloadManager is null || offered is null || offered.TargetFullRelease.Version.ToNormalizedString() != version)
            throw new InvalidOperationException("Check this release before downloading it.");
        UpdatePackageValidation.Validate(offered.TargetFullRelease);
        // Velopack verifies the advertised package hash before exposing UpdatePendingRestart.
        await downloadManager.DownloadUpdatesAsync(offered, progress, cancellation);
    }
}
