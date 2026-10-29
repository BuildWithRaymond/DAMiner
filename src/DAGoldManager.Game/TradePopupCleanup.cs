using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Identifies only the exchange-completion popup, leaving unrelated dialogs untouched.</summary>
public static class TradePopupCleanup
{
    public static bool IsCompletion(string text)
    {
        string message = text.Trim(' ', '\t', '\r', '\n', '(', ')');
        if (message.EndsWith('.') || message.EndsWith('!')) message = message[..^1];
        return message.Equals("Exchange completed", StringComparison.OrdinalIgnoreCase) ||
            message.Equals("You have exchanged", StringComparison.OrdinalIgnoreCase);
    }

    // Chat can quote the same words; only server notices carry completion evidence.
    public static bool IsServerCompletion(byte type, string text) =>
        type is 1 or 2 or 3 or 4 or 6 or 8 or 9 && IsCompletion(text);

    public static ServerNotice? CompletionPopup(CharacterSnapshot state, long generation, long beforeRevision) =>
        state.Generation == generation && state.Popup is { } popup && popup.Generation == generation &&
        popup.Revision > beforeRevision && popup.Type is 8 or 9 && IsCompletion(popup.Text) ? popup : null;
}
