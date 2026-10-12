using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Combines exact balance changes with the evidence required for local or external recipients.</summary>
public static class TradeConfirmation
{
    public static async Task WaitAsync(Func<TradeView> trade, Func<CharacterSnapshot> donor,
        Func<CharacterSnapshot>? recipient, CharacterSnapshot donorBefore, CharacterSnapshot? recipientBefore,
        int bars, uint gold, string barName, TimeSpan timeout, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        try
        {
            while (true)
            {
                limit.Token.ThrowIfCancellationRequested();
                var view = trade();
                if (!view.Started || view.Cancelled || view.UnexpectedItem)
                    throw new InvalidOperationException("Trade was replaced, cancelled, or contains unexpected items.");
                bool donorChanged = Matches(donorBefore, donor(), bars, gold, barName, receiving: false);
                bool recipientChanged = recipient is not null && recipientBefore is not null &&
                    Matches(recipientBefore, recipient(), bars, gold, barName, receiving: true);
                // Both managed balances are stronger evidence than an optional acceptance echo.
                // External recipients require explicit acceptance/completion plus the courier's change.
                bool completed = recipient is null ? view.Completed || view.LocalAccepted && view.PartnerAccepted : recipientChanged;
                if (donorChanged && completed) return;
                await Task.Delay(25, limit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("Trade outcome was not confirmed by exact balance changes. Inspect the game before another run."); }
    }

    static bool Matches(CharacterSnapshot before, CharacterSnapshot after, int bars, uint gold,
        string barName, bool receiving)
    {
        if (!after.Connected || after.Generation != before.Generation || after.Id != before.Id ||
            !string.Equals(after.Name, before.Name, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Trade participant disconnected or changed identity.");
        if (after.Revision <= before.Revision || !after.InventoryKnown || after.Gold is null || before.Gold is null)
            return false;
        long goldChange = (long)after.Gold.Value - before.Gold.Value;
        int barChange = after.Count(barName) - before.Count(barName);
        return goldChange == (receiving ? gold : -(long)gold) && barChange == (receiving ? bars : -bars);
    }
}
