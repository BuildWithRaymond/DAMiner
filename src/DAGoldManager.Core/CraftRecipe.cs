namespace DAGoldManager.Core;

public enum DialogActionKind { Choice, Next, Input }
/// <summary>A semantic menu step guarded by the dialog text observed during recording.</summary>
public sealed record RecordedDialogAction(DialogActionKind Kind, string ExpectedText, string Value = "");
/// <summary>A demonstrated one-bar purchase, promoted only after verified gold and inventory changes.</summary>
public sealed record CraftRecipe(string NpcName, int Map, int X, int Y, string BarName, long GoldCost,
    RecordedDialogAction[] Actions, DateTimeOffset RecordedAt, int CooldownSeconds = 30)
{
    // Servers can display a different item name in the trade window than in inventory.
    public string? OfferName { get; init; }
}

public sealed record GameSettings
{
    public string ClientPath { get; init; } = "";
    public string ServerAddress { get; init; } = "52.88.55.94";
    public int ServerPort { get; init; } = 2610;
    public string ServerProfile { get; init; } = "datester-7.41";
    public int MovementMilliseconds { get; init; } = 400;
    public int BarWeight { get; init; } = 1;
    public int TradeTimeoutSeconds { get; init; } = 120;
}

/// <summary>Recoverable recording progress; zero batch size is not ready for an automated run.</summary>
public sealed record RecordingDraft(string Character, CraftRecipe Recipe, int VerifiedBatchSize = 0, string? Error = null);

/// <summary>Serializable settings and recipe. Credentials are intentionally absent.</summary>
public sealed record AppConfiguration(SavedCharacter[] Characters, RunSettings Run, GameSettings Game,
    CraftRecipe? Recipe = null, RecordingDraft? Draft = null);

public static class TransferVerification
{
    public static bool IsComplete(bool localAccepted, bool partnerAccepted, bool serverCompleted,
        int expectedBars, int beforeBars, int afterBars)
        => expectedBars > 0 && beforeBars - afterBars == expectedBars &&
            (serverCompleted || localAccepted && partnerAccepted);
}
