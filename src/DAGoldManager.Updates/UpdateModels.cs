namespace DAGoldManager.Updates;

public sealed record UpdatePreferences(string Repository = "", bool Automatic = true);
public enum UpdatePhase { NotConfigured, Disabled, Idle, Checking, Downloading, Ready, Current, Failed, Unavailable }
public sealed record UpdateStatus(UpdatePhase Phase, string Message, string? Version = null, int Percent = 0);

/// <summary>Check/download boundary; installation is intentionally restricted to desktop startup.</summary>
public interface IUpdateClient
{
    bool IsInstalled { get; }
    string? UnavailableReason => null;
    /// <summary>A version becomes pending only after package download and validation succeed.</summary>
    string? PendingVersion { get; }
    Task<string?> CheckAsync(string repository, CancellationToken cancellation);
    Task DownloadAsync(string version, Action<int> progress, CancellationToken cancellation);
}
