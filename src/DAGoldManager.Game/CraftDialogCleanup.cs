using DAGoldManager.Core;
using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Recognizes informational recipe pages that may be closed after a purchase is verified.</summary>
public static class CraftDialogCleanup
{
    public static async Task FinishAsync(CraftRecipe recipe, Func<CharacterSnapshot> snapshot,
        Func<GameCommand, CancellationToken, Task> execute, CancellationToken cancellation)
    {
        var seen = new HashSet<(long Generation, long Revision)>();
        for (int page = 0; page < 12; page++)
        {
            cancellation.ThrowIfCancellationRequested();
            var state = snapshot();
            var dialog = state.Dialog;
            if (dialog is null) return;
            if (!state.Connected || state.Generation != dialog.Generation)
                throw new InvalidOperationException("Connection changed while finishing bank confirmation.");
            if (state.Map != recipe.Map || !dialog.Name.Equals(recipe.NpcName, StringComparison.OrdinalIgnoreCase)) return;
            if (!seen.Add((dialog.Generation, dialog.Revision)))
                throw new InvalidOperationException("Bank confirmation did not advance. No craft was retried.");
            // Only informational Next pages are safe. Never select another purchase or enter an amount.
            bool next = dialog.IsPursuit && dialog.HasNext && dialog.Choices.Length == 0 && dialog.Prompt is null;
            await execute(new(next ? "next" : "close", Dialog: dialog), cancellation);
            if (!next) return;
        }
        throw new InvalidOperationException("Bank confirmation exceeded 12 pages. No craft was retried.");
    }
}
