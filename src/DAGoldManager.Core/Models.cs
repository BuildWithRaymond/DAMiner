namespace DAGoldManager.Core;

/// <summary>A saved roster entry. Passwords deliberately live in a separate Windows vault.</summary>
public sealed record SavedCharacter(string Name, bool Enabled = true, bool CanCraftBars = true);

/// <summary>A scanned character balance; BarCapacity is additional carrying room, not total slots.</summary>
/// <remarks>Registered is null when the server has not established registration status.</remarks>
public sealed record CharacterState(string Name, long Gold, int Bars, int BarCapacity, int Map, int X, int Y, bool? Registered = null);

/// <summary>External bankers are controlled elsewhere; local bankers are managed by this run.</summary>
public enum BankerMode { External, Local }

/// <summary>Workflow choices without UI controls, Windows paths, or game packet details.</summary>
public sealed record RunSettings
{
    // One courier collects producer inventory and is the only sender to the final banker.
    public string Courier { get; init; } = "";
    public string Recipient { get; init; } = "";
    // Zero remains External so profiles saved before this option retain VPS behavior.
    public BankerMode BankerMode { get; init; } = BankerMode.External;
    // Retained for old profile compatibility. Local deposits always use Lamont in Abel bank.
    public string DepositNpcName { get; init; } = "";
    // External destinations are hints. A local banker is approached at its observed position.
    public int RecipientMap { get; init; }
    public int RecipientX { get; init; }
    public int RecipientY { get; init; }
    // Recording establishes a proven batch size; inventory/weight may reduce it further.
    public int BarsPerTrade { get; init; } = 1;
    // Only external bankers need a timed delay; local deposits are confirmed individually.
    public int DepositWaitSeconds { get; init; } = 180;
    public long BarCost { get; init; } = GoldPlanner.DefaultBarCost;
    public bool PoolLeftoverGold { get; init; } = true;
}

/// <summary>Progress visible to the user; it is not proof that an operation succeeded.</summary>
public enum RunPhase
{
    Idle, Launching, Scanning, Crafting, PoolingGold, Collecting, Delivering,
    WaitingForDeposit, Completed, NeedsReview, Stopped, WaitingForCollection
}

/// <summary>Append-only transaction outcomes used for crash recovery and human review.</summary>
public enum OperationOutcome
{
    Planned,     // Intent is durable; no command has been issued yet.
    Started,     // A command may have reached the server; never automatically replay it.
    Verified,    // Fresh evidence establishes the exact expected change.
    NeedsReview, // The outcome is ambiguous and must be inspected before another run.
    Rejected     // A registration refusal was confirmed with unchanged balances.
}

public sealed record RunProgress(RunPhase Phase, string Character, string Message, int Created = 0, int Delivered = 0);
public sealed record RunResult(long StartingGold, long EndingGold, int StartingBars, int CreatedBars, int DeliveredBars);
/// <summary>One immutable ledger entry. Repeated OperationId values describe the same operation.</summary>
public sealed record OperationRecord(Guid RunId, Guid OperationId, string Kind, string Sender, string Receiver,
    long Amount, OperationOutcome Outcome, DateTimeOffset Time, string Detail = "");

// Only this boundary knows how the game is controlled. Core code can run against a simulator.
public interface IGameGateway
{
    Task LaunchAsync(SavedCharacter character, CancellationToken cancellation);
    /// <summary>Read confirmed game state, not an optimistic prediction of sent commands.</summary>
    Task<CharacterState> ReadAsync(string name, CancellationToken cancellation);
    Task WalkToBankAsync(string name, CancellationToken cancellation);
    Task ApproachCharacterAsync(string sender, string receiver, CancellationToken cancellation);
    Task WalkToRecipientAsync(string courier, RunSettings settings, CancellationToken cancellation);
    /// <summary>Make one bar and wait for fresh evidence; do not retry an uncertain purchase.</summary>
    Task CraftBarAsync(string name, CancellationToken cancellation);
    // Cleanup is separate from spending so its failure cannot erase an already verified craft.
    Task FinishCraftDialogsAsync(string name, CancellationToken cancellation) => Task.CompletedTask;
    Task TransferGoldAsync(string sender, string receiver, long amount, CancellationToken cancellation);
    Task TransferBarsAsync(string sender, string receiver, int amount, CancellationToken cancellation);
    Task FinishTradeDialogsAsync(string sender, string receiver, CancellationToken cancellation) => Task.CompletedTask;
    Task PrepareBankerAsync(string name, RunSettings settings, CancellationToken cancellation)
        => throw new NotSupportedException("This gateway cannot manage a local banker.");
    Task DepositBarAsync(string name, RunSettings settings, CancellationToken cancellation)
        => throw new NotSupportedException("This gateway cannot deposit bars.");
    Task FinishDepositDialogsAsync(string name, CancellationToken cancellation) => Task.CompletedTask;
}

/// <summary>Durable append-only history. Core does not depend on a database implementation.</summary>
public interface IOperationLedger
{
    Task AppendAsync(OperationRecord operation, CancellationToken cancellation);
}

/// <summary>Injectable time lets tests exercise deposit waits without waiting real minutes.</summary>
public interface IRunClock
{
    DateTimeOffset Now { get; }
    Task DelayAsync(TimeSpan duration, CancellationToken cancellation);
}

public sealed class SystemRunClock : IRunClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellation) => Task.Delay(duration, cancellation);
}
