using DAGoldManager.Core;
using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Checks that the managed banker reached Abel and can see the expected non-player merchant.</summary>
public static class BankerPreparation
{
    public static async Task PrepareAsync(Func<CharacterSnapshot> snapshot,
        Func<GameCommand, CancellationToken, Task> execute,
        Func<CancellationToken, Task> waitForNpc, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Always finish walking to the safe floor before considering NPC readiness.
        await execute(BankNavigation.AbelArrival(), cancellation);
        if (!NpcVisible(snapshot())) await waitForNpc(cancellation);
        cancellation.ThrowIfCancellationRequested();
        RequireNpc(snapshot());
    }

    public static bool NpcVisible(CharacterSnapshot state) =>
        state.Connected && state.Map == AbelBank.Map && state.Entities.Values.Any(IsLamont);

    public static Entity RequireNpc(CharacterSnapshot state)
    {
        if (!state.Connected || state.Map != AbelBank.Map)
            throw new InvalidOperationException("Local banker must be connected inside Abel bank before depositing.");
        var matches = state.Entities.Values.Where(IsLamont).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Local banker must see exactly one Lamont inside Abel bank. No deposit was sent.");
        return matches[0];
    }

    static bool IsLamont(Entity entity) => !entity.IsPlayer && entity.Id != 0 &&
        entity.Name.Equals(AbelBank.NpcName, StringComparison.OrdinalIgnoreCase);
}
