using DAGoldManager.Core;
using DAGoldManager.Storage;
using Xunit;

namespace DAGoldManager.Tests;

public class LocalStoreTests
{
    [Fact]
    public async Task InterruptedOperationSurvivesRestartAndBlocksBlindReplay()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAGoldManagerTests", Guid.NewGuid().ToString("N"));
        var store = new LocalStore(directory);
        var operation = new OperationRecord(Guid.NewGuid(), Guid.NewGuid(), "craft", "Fictional", "", 1,
            OperationOutcome.Started, DateTimeOffset.UtcNow);
        await store.AppendAsync(operation, default);
        var reopened = new LocalStore(directory);
        Assert.True(reopened.HasUnresolvedOperations());
        reopened.AcknowledgeUnresolvedOperations();
        Assert.False(reopened.HasUnresolvedOperations());
        Assert.Single(reopened.RecentOperations());
    }

    [Fact]
    public void ConfigurationRoundTripsWithoutPasswordFields()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAGoldManagerTests", Guid.NewGuid().ToString("N"));
        var store = new LocalStore(directory);
        store.Save(new([new("Fictional")], new() { Courier = "Fictional" }, new()));
        var loaded = new LocalStore(directory).Load();
        Assert.Equal("Fictional", Assert.Single(loaded.Characters).Name);
        Assert.Equal("Fictional", loaded.Run.Courier);
    }
}
