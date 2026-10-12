using DAGoldManager.Updates;
using Xunit;

namespace DAGoldManager.Tests;

public class ApplicationUpdatesTests
{
    static string DirectoryPath() => Path.Combine(Path.GetTempPath(), "DAMinerUpdateTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EmptySourceDoesNotMakeNetworkRequests()
    {
        var client = new TestUpdateClient();
        using var updates = new ApplicationUpdates(client, DirectoryPath(), "0.2.0");
        await updates.CheckAsync();
        Assert.Equal(UpdatePhase.NotConfigured, updates.Status.Phase);
        Assert.Equal(0, client.Checks);
    }

    [Fact]
    public async Task NewerReleaseDownloadsAndWaitsForNextLaunch()
    {
        var client = new TestUpdateClient { Latest = "0.3.0" };
        using var updates = new ApplicationUpdates(client, DirectoryPath(), "0.2.0", "example/DAMiner");
        await updates.CheckAsync(automatic: true);
        Assert.Equal(1, client.Downloads);
        Assert.Equal(UpdatePhase.Ready, updates.Status.Phase);
        Assert.Equal("0.3.0", updates.Status.Version);
        Assert.Contains("next", updates.Status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.2.0")]
    public async Task OlderOrEqualReleasesNeverDownload(string version)
    {
        var client = new TestUpdateClient { Latest = version };
        using var updates = new ApplicationUpdates(client, DirectoryPath(), "0.2.0", "example/DAMiner");
        await updates.CheckAsync();
        Assert.Equal(0, client.Downloads);
        Assert.Equal(UpdatePhase.Current, updates.Status.Phase);
    }

    [Fact]
    public async Task OfflineCheckIsContainedInUpdateStatus()
    {
        var client = new TestUpdateClient { Failure = new HttpRequestException("Offline") };
        using var updates = new ApplicationUpdates(client, DirectoryPath(), "0.2.0", "example/DAMiner");
        await updates.CheckAsync();
        Assert.Equal(UpdatePhase.Failed, updates.Status.Phase);
        Assert.False(updates.Working);
        Assert.Equal(0, client.Downloads);
    }

    [Fact]
    public async Task FailedVerificationNeverReportsUpdateReady()
    {
        var client = new TestUpdateClient { Latest = "0.3.0", DownloadFailure = new InvalidDataException("Checksum mismatch") };
        using var updates = new ApplicationUpdates(client, DirectoryPath(), "0.2.0", "example/DAMiner");
        await updates.CheckAsync();
        Assert.Equal(UpdatePhase.Failed, updates.Status.Phase);
        Assert.Null(client.PendingVersion);
    }

    [Fact]
    public async Task DisabledAutomaticChecksStillAllowManualChecks()
    {
        var client = new TestUpdateClient();
        using var updates = new ApplicationUpdates(client, DirectoryPath(), "0.2.0", "example/DAMiner");
        updates.SavePreferences(new("example/DAMiner", Automatic: false));
        await updates.CheckAsync(automatic: true);
        Assert.Equal(0, client.Checks);
        await updates.CheckAsync();
        Assert.Equal(1, client.Checks);
    }

    [Fact]
    public async Task ConcurrentChecksCannotDownloadTwiceOrChangeTheSource()
    {
        var client = new TestUpdateClient { Latest = "0.3.0", WaitForCheck = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var updates = new ApplicationUpdates(client, DirectoryPath(), "0.2.0", "example/DAMiner");
        var first = updates.CheckAsync();
        await updates.CheckAsync();
        Assert.Equal(1, client.Checks);
        Assert.Throws<InvalidOperationException>(() => updates.SavePreferences(new("another/DAMiner")));
        client.WaitForCheck.SetResult();
        await first;
        Assert.Equal(1, client.Downloads);
    }

    [Fact]
    public async Task PreviewAndRawBuildNeverContactReleaseSource()
    {
        using var preview = new ApplicationUpdates(null, DirectoryPath(), "0.2.0", "example/DAMiner");
        await preview.CheckAsync();
        Assert.True(preview.Preview);
        Assert.Equal(UpdatePhase.Disabled, preview.Status.Phase);
        var client = new TestUpdateClient { IsInstalled = false };
        using var raw = new ApplicationUpdates(client, DirectoryPath(), "0.2.0", "example/DAMiner");
        await raw.CheckAsync();
        Assert.Equal(UpdatePhase.Unavailable, raw.Status.Phase);
        Assert.Equal(0, client.Checks);
    }

    [Fact]
    public void PreferencesPersistAndPendingUpdatePreventsChangingRepository()
    {
        string directory = DirectoryPath();
        var client = new TestUpdateClient();
        using (var updates = new ApplicationUpdates(client, directory, "0.2.0"))
            updates.SavePreferences(new("example/DAMiner", Automatic: false));
        client.PendingVersion = "0.3.0";
        using var restored = new ApplicationUpdates(client, directory, "0.2.0");
        Assert.Equal("example/DAMiner", restored.Preferences.Repository);
        Assert.False(restored.Preferences.Automatic);
        Assert.Equal(UpdatePhase.Ready, restored.Status.Phase);
        Assert.Throws<InvalidOperationException>(() => restored.SavePreferences(new("another/DAMiner")));
    }

    [Theory]
    [InlineData("example/DAMiner", "example/DAMiner")]
    [InlineData(" https://github.com/example/DAMiner/ ", "example/DAMiner")]
    [InlineData("https://github.com/example/DAMiner.git", "example/DAMiner")]
    public void PublicRepositoryFormsNormalize(string value, string expected) => Assert.Equal(expected, GitHubRepository.Normalize(value));

    [Theory]
    [InlineData("https://other.example/example/DAMiner")]
    [InlineData("https://name:secret@github.com/example/DAMiner")]
    [InlineData("example/../DAMiner")]
    [InlineData("example/DAMiner?token=secret")]
    public void InvalidOrCredentialBearingSourcesAreRejected(string value) => Assert.Throws<ArgumentException>(() => GitHubRepository.Normalize(value));
}

internal sealed class TestUpdateClient : IUpdateClient
{
    public bool IsInstalled { get; init; } = true;
    public string? PendingVersion { get; set; }
    public string? Latest { get; init; }
    public Exception? Failure { get; init; }
    public Exception? DownloadFailure { get; init; }
    public TaskCompletionSource? WaitForCheck { get; init; }
    public int Checks, Downloads;
    public async Task<string?> CheckAsync(string repository, CancellationToken cancellation)
    {
        Checks++;
        if (WaitForCheck is not null) await WaitForCheck.Task.WaitAsync(cancellation);
        if (Failure is not null) throw Failure;
        return Latest;
    }
    public Task DownloadAsync(string version, Action<int> progress, CancellationToken cancellation)
    {
        Downloads++;
        if (DownloadFailure is not null) throw DownloadFailure;
        progress(100);
        PendingVersion = version;
        return Task.CompletedTask;
    }
}
