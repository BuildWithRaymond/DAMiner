using DAGoldManager.Core;
using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Learns semantic dialog steps and sample trade limits from fresh server snapshots.</summary>
/// <remarks>Reopening an NPC never discards a previously verified recipe.</remarks>
public sealed class SetupRecorder
{
    public CraftRecipe? Recipe { get; private set; }
    public int VerifiedBatchSize { get; private set; }
    public IReadOnlyList<string> Events { get { lock (events) return events.ToArray(); } }
    readonly List<string> events = [];
    readonly List<RecordedDialogAction> actions = [];
    CharacterSnapshot? baseline;
    Entity? activeNpc;
    DateTimeOffset? previousCraft;
    int observedCooldown = 30;

    public SetupRecorder(CraftRecipe? savedRecipe = null, int verifiedBatchSize = 0)
    {
        Recipe = savedRecipe;
        if (savedRecipe is null) return;
        VerifiedBatchSize = verifiedBatchSize;
        observedCooldown = savedRecipe.CooldownSeconds;
        Log($"Restored verified craft: {savedRecipe.NpcName}, {savedRecipe.GoldCost:N0} gold per {savedRecipe.BarName}.");
        if (verifiedBatchSize > 0) Log($"Retained verified sample batch: {verifiedBatchSize} bars.");
    }

    public void BeginNpc(CharacterSnapshot before, Entity npc)
    {
        if (!before.Connected || !before.InventoryKnown || before.Gold is null || before.Map is null || npc.IsPlayer)
            throw new InvalidOperationException("Record with a fully scanned character and a visible NPC.");
        baseline = before;
        activeNpc = npc;
        actions.Clear();
        Log($"Opened {npc.Name} on map {before.Map} at ({npc.Position.X},{npc.Position.Y}).");
    }

    public void RecordAction(RecordedDialogAction action)
    {
        if (baseline is null) return;
        // Crafting amount prompts are numeric. Never retain arbitrary typed text.
        if (action.Kind == DialogActionKind.Input &&
            (action.Value.Length is < 1 or > 12 || !action.Value.All(char.IsAsciiDigit)))
            throw new InvalidOperationException("Only numeric crafting inputs can be saved in a recipe.");
        if (actions.Count >= 30) throw new InvalidOperationException("Craft recording exceeded 30 dialog actions. Start a new recording.");
        actions.Add(action with { ExpectedText = DialogText.ForCharacter(action.ExpectedText, baseline.Name ?? "") });
        Log(action.Kind == DialogActionKind.Choice ? $"Chose {action.Value}." : $"Recorded {action.Kind} action.");
    }

    public void ObserveState(CharacterSnapshot state)
    {
        // A later snapshot in the same connection generation must prove a single new item and
        // its gold cost. Sending Yes or seeing a dialog is not evidence that a purchase occurred.
        if (baseline is null || activeNpc is null || state.Generation != baseline.Generation ||
            !state.Connected || !state.InventoryKnown || state.Revision <= baseline.Revision || actions.Count == 0)
            return;
        long cost = (long)(baseline.Gold ?? 0) - (state.Gold ?? baseline.Gold ?? 0);
        if (cost <= 0 || cost > GoldPlanner.CarryingLimit) return;
        var additions = state.Inventory.Values.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => (Name: name, Added: state.Count(name) - baseline.Count(name))).Where(i => i.Added > 0).ToArray();
        if (additions.Length != 1 || additions[0].Added != 1) return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool sameItem = Recipe is not null && Recipe.BarName.Equals(additions[0].Name, StringComparison.OrdinalIgnoreCase) && Recipe.GoldCost == cost;
        string? previousOfferName = sameItem ? Recipe?.OfferName : null;
        if (!sameItem) VerifiedBatchSize = 0;
        int cooldown = previousCraft is null ? observedCooldown : Math.Max(observedCooldown,
            Math.Clamp((int)Math.Ceiling((now - previousCraft.Value).TotalSeconds), 1, 3600));
        Recipe = new(activeNpc.Name, baseline.Map!.Value, activeNpc.Position.X, activeNpc.Position.Y,
            additions[0].Name, cost, actions.ToArray(), now, cooldown) { OfferName = previousOfferName };
        previousCraft = now;
        baseline = null;
        Log($"Verified one {Recipe.BarName} for {cost:N0} gold. Craft recipe ready.");
    }

    public void RecordVerifiedTrade(int bars)
    {
        if (bars is < 1 or > 59) throw new ArgumentOutOfRangeException(nameof(bars));
        VerifiedBatchSize = Math.Max(VerifiedBatchSize, bars);
        Log($"Verified sample trade containing {bars} bars.");
    }

    public void RecordVerifiedTrade(int bars, string offerName)
    {
        if (string.IsNullOrWhiteSpace(offerName)) throw new InvalidOperationException("Trade display name is missing.");
        RecordVerifiedTrade(bars);
        if (Recipe is not null) Recipe = Recipe with { OfferName = offerName };
    }

    public void RecordCooldownNotice(string text)
    {
        if (activeNpc is null) return;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(?:wait|every|after).*?(\d+)\s*(seconds?|minutes?)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int value)) return;
        if (match.Groups[2].Value.StartsWith("minute", StringComparison.OrdinalIgnoreCase)) value = Math.Min(value, 60) * 60;
        observedCooldown = Math.Max(observedCooldown, Math.Clamp(value, 1, 3600));
        if (Recipe is not null) Recipe = Recipe with { CooldownSeconds = observedCooldown };
        Log($"Observed crafting wait hint: {observedCooldown} seconds. Server outcomes still determine success.");
    }

    void Log(string text) { lock (events) events.Add(text); }
}
