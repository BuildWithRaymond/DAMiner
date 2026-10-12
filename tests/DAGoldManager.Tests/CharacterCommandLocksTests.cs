using DAGoldManager.Game;
using Xunit;

namespace DAGoldManager.Tests;

public class CharacterCommandLocksTests
{
    [Fact]
    public async Task DifferentCharactersCanActWhileFirstIsStillBusy()
    {
        var locks = new CharacterCommandLocks();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = locks.RunAsync(["First"], async () => { entered.SetResult(); await release.Task; }, default);
        await entered.Task;
        var second = locks.RunAsync(["Second"], () => Task.CompletedTask, default);
        bool overlapped = await Task.WhenAny(second, Task.Delay(1000)) == second;
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(overlapped, "Another character must not wait on a global gameplay lock.");
    }

    [Fact]
    public async Task TradeLocksBothCharactersAndIgnoresNameCase()
    {
        var locks = new CharacterCommandLocks();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trade = locks.RunAsync(["Donor", "Courier"], () => release.Task, default);
        var craft = locks.RunAsync(["courier"], () => Task.CompletedTask, default);
        Assert.False(craft.IsCompleted);
        release.SetResult();
        await Task.WhenAll(trade, craft);
    }

    [Fact]
    public async Task CancelledTradeReleasesAnAlreadyAcquiredParticipant()
    {
        var locks = new CharacterCommandLocks();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = locks.RunAsync(["Zulu"], () => release.Task, default);
        using var cancellation = new CancellationTokenSource();
        var trade = locks.RunAsync(["Alpha", "Zulu"], () => Task.CompletedTask, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trade);
        try { await locks.RunAsync(["Alpha"], () => Task.CompletedTask, default).WaitAsync(TimeSpan.FromSeconds(2)); }
        finally { release.SetResult(); await busy; }
    }
}
