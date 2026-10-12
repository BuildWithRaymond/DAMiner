using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Identifies only the exchange-completion popup, leaving unrelated dialogs untouched.</summary>
public static class TradePopupCleanup
{
    public static bool IsCompletion(string text) =>
        text.Trim(' ', '\r', '\n', '(', ')').Equals("Exchange completed.", StringComparison.OrdinalIgnoreCase);

    public static ServerNotice? CompletionPopup(CharacterSnapshot state, long generation, long beforeRevision) =>
        state.Generation == generation && state.Popup is { } popup && popup.Generation == generation &&
        popup.Revision > beforeRevision && popup.Type is 8 or 9 && IsCompletion(popup.Text) ? popup : null;
}
