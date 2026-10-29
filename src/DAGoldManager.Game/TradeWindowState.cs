using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;

namespace DAGoldManager.Game;

/// <summary>Retains open-window evidence between attempts, including manually opened exchanges.</summary>
public sealed class TradeWindowState
{
    readonly object gate = new();
    bool open;
    bool uncertain;
    public bool IsOpen { get { lock (gate) return open; } }
    public bool BlocksNewTrade { get { lock (gate) return open || uncertain; } }

    public void MarkUncertain() { lock (gate) uncertain = true; }

    public void ObserveCompletion(string text)
    {
        lock (gate)
        {
            // A server system message can close the current window without an
            // exchange packet. This is window state, not proof of transferred balances.
            if (open && TradePopupCleanup.IsCompletion(text)) { open = false; uncertain = false; }
        }
    }

    public void Observe(ServerExchangeMessage message)
    {
        lock (gate)
        {
            if (message.Event == ExchangeServerEventType.Started) { open = true; uncertain = false; }
            else if (message.Event == ExchangeServerEventType.Cancelled ||
                message.Event == ExchangeServerEventType.Accepted &&
                ((byte?)message.Party == 2 || TradePopupCleanup.IsCompletion(message.Message ?? ""))) { open = false; uncertain = false; }
        }
    }

    // Both exact managed balances (or the external completion evidence) have
    // already confirmed this trade; this does not publish fabricated game state.
    public void ConfirmVerifiedCompletion() { lock (gate) { open = false; uncertain = false; } }
}
