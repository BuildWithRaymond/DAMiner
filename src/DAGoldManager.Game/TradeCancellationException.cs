namespace DAGoldManager.Game;

/// <summary>Keeps the trade stage in history while preserving cancellation semantics.</summary>
public sealed class TradeCancellationException : OperationCanceledException
{
    public TradeCancellationException(string stage, bool acceptanceStarted, bool stopRequested,
        OperationCanceledException original)
        : base(Describe(stage, acceptanceStarted, stopRequested), original, original.CancellationToken) { }

    static string Describe(string stage, bool acceptanceStarted, bool stopRequested)
    {
        string cause = stopRequested ? "Stop requested." : "Session interruption cancelled the trade.";
        // Attempted acceptance may have reached the server even when the write was cancelled.
        string outcome = acceptanceStarted ? "Acceptance may have reached the server. Inspect the game before another run." :
            "Acceptance was not attempted. Inspect any open exchange before another run.";
        return $"Trade stopped while {stage}: {cause} {outcome}";
    }
}
