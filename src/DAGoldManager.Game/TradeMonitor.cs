using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using System.Text.RegularExpressions;

namespace DAGoldManager.Game;

public sealed record TradeView(bool Started, uint TargetId, string TargetName, int OwnBars, int OtherBars,
    uint OwnGold, uint OtherGold, bool LocalAccepted, bool PartnerAccepted, bool Completed, bool Cancelled, bool UnexpectedItem, byte PromptSlot);

/// <summary>Tracks the server's current exchange partner, contents, and acceptance state.</summary>
public sealed class TradeMonitor(string barName)
{
    readonly object gate = new();
    readonly Dictionary<byte, int> ownItems = [];
    readonly Dictionary<byte, int> otherItems = [];
    TradeView view = new(false, 0, "", 0, 0, 0, 0, false, false, false, false, false, 0);
    public TradeView View { get { lock (gate) return view; } }

    public void ObserveCompletion(string text)
    {
        lock (gate)
        {
            if (view.Started && !view.Cancelled && TradePopupCleanup.IsCompletion(text))
                view = view with { Completed = true };
        }
    }

    public void Observe(ServerExchangeMessage message)
    {
        lock (gate)
        {
            switch (message.Event)
            {
                case ExchangeServerEventType.Started:
                    ownItems.Clear();
                    otherItems.Clear();
                    view = new(true, message.TargetId ?? 0, message.TargetName ?? "", 0, 0, 0, 0, false, false, false, false, false, 0);
                    break;
                case ExchangeServerEventType.QuantityPrompt:
                    view = view with { PromptSlot = message.Slot ?? 0 };
                    break;
                case ExchangeServerEventType.ItemAdded:
                    int quantity = BarQuantity(message.ItemName ?? "");
                    if (quantity == 0 || message.ItemIndex is null)
                    {
                        view = view with { UnexpectedItem = true };
                        break;
                    }
                    var items = message.Party == ExchangeParty.You ? ownItems : otherItems;
                    items[message.ItemIndex.Value] = quantity;
                    view = view with { OwnBars = ownItems.Values.Sum(), OtherBars = otherItems.Values.Sum(), PromptSlot = 0,
                        LocalAccepted = false, PartnerAccepted = false };
                    break;
                case ExchangeServerEventType.GoldAdded:
                    view = message.Party == ExchangeParty.You ? view with { OwnGold = message.GoldAmount ?? 0 } :
                        view with { OtherGold = message.GoldAmount ?? 0 };
                    view = view with { LocalAccepted = false, PartnerAccepted = false };
                    break;
                case ExchangeServerEventType.Accepted:
                    view = view with
                    {
                        LocalAccepted = view.LocalAccepted || message.Party == ExchangeParty.You,
                        PartnerAccepted = view.PartnerAccepted || message.Party == ExchangeParty.Them,
                        Completed = view.Completed || (byte?)message.Party == 2 || TradePopupCleanup.IsCompletion(message.Message ?? "")
                    };
                    break;
                case ExchangeServerEventType.Cancelled:
                    view = view with { Cancelled = true };
                    break;
            }
        }
    }

    int BarQuantity(string name)
    {
        if (name.Equals(barName, StringComparison.OrdinalIgnoreCase)) return 1;
        var match = Regex.Match(name, "^" + Regex.Escape(barName) + @"\((\d+)\)$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int count) && count > 0 ? count : 0;
    }
}
