using DAQuester.Core;
using DAQuester.DarkAges.Navigation;

namespace DAQuester.DarkAges;

public sealed partial class GameSession
{
    static Entity? FindTravelNpc(CharacterSnapshot state, NpcTransfer transfer)
    {
        var matches = state.Entities.Values.Where(entity => !entity.IsPlayer &&
            entity.Name.Equals(transfer.Npc, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
            throw new QuestBlockedException("More than one " + transfer.Npc + " is visible. Review the travel NPC before resuming.");
        return matches.SingleOrDefault();
    }

    static bool TravelApproachStable(CharacterSnapshot entry, CharacterSnapshot state, int map) =>
        state.Connected && state.Generation == entry.Generation && state.Id == entry.Id && state.Name == entry.Name &&
        state.Map == map && state.Position is not null && state.Dialog is null &&
        !(state.Popup is { } popup && popup != entry.Popup && popup.Generation == state.Generation && popup.Revision > entry.Revision);

    HashSet<Tile> TravelApproachObstacles(CharacterSnapshot state, TravelStep step)
    {
        var map = maps.Record(step.SourceMap);
        // The anchor is an NPC location estimate, never permission to step onto it
        // while the NPC's draw packet is still outside the visible area.
        return state.Entities.Values.Where(entity => entity.Blocks && entity.Id != state.Id).Select(entity => entity.Position)
            .Concat(map.ExitTiles).Concat(map.InternalTransfers.Select(transfer => transfer.From))
            .Append(step.NpcTransfer!.Anchor).ToHashSet();
    }

    QuestBlockedException TravelApproachFailure(string reason, CharacterSnapshot entry, TravelStep step)
    {
        var state = Snapshot;
        var transfer = step.NpcTransfer!;
        string Point(Tile? tile) => tile is null ? "unknown" : $"({tile.X},{tile.Y})";
        var observed = state.Entities.Values.Where(entity => !entity.IsPlayer &&
            entity.Name.Equals(transfer.Npc, StringComparison.OrdinalIgnoreCase)).Select(entity => Point(entity.Position));
        string npc = string.Join(", ", observed);
        return new($"{reason} Travel NPC {transfer.Npc}; map {step.SourceMap}, start {Point(entry.Position)}, " +
            $"current map {state.Map}, position {Point(state.Position)}, anchor {Point(transfer.Anchor)}, observed NPC " +
            (npc.Length == 0 ? "unseen." : npc + "."));
    }

    void ValidateTravelApproach(CharacterSnapshot entry, TravelStep step, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!TravelApproachStable(entry, Snapshot, step.SourceMap))
            throw TravelApproachFailure("Travel approach owner, location or popup changed. Popup is preserved; no NPC opened.", entry, step);
    }

    async Task WalkTravelApproach(Tile goal, CharacterSnapshot entry, TravelStep step, CancellationToken ct)
    {
        var map = maps.Record(step.SourceMap);
        for (int moves = 0; moves < checked(map.Width * map.Height); moves++)
        {
            ValidateTravelApproach(entry, step, ct);
            var before = Snapshot;
            if (before.Position == goal || TravelNpcVisible(step.SourceMap, step.NpcTransfer!.Npc)) return;
            Tile next;
            try
            {
                next = maps.Load(step.SourceMap).Path(before.Position!, goal, TravelApproachObstacles(before, step)).First();
            }
            catch (InvalidOperationException)
            {
                throw TravelApproachFailure("Travel approach has no reachable free interior path.", entry, step);
            }
            // One adjacent goal preserves the ordinary acknowledged walker. Stop
            // its replanning if occupancy changes, so a detour cannot cross the anchor.
            await WalkTo(next, ct, step.SourceMap, replanWhen: () =>
                !TravelApproachStable(entry, Snapshot, step.SourceMap) || Snapshot.Position != before.Position ||
                !ReferenceEquals(Snapshot.Entities, before.Entities));
            ValidateTravelApproach(entry, step, ct);
        }
        throw TravelApproachFailure("Travel approach exceeded its movement bound.", entry, step);
    }

    async Task<Entity?> ApproachObservedTravelNpc(CharacterSnapshot entry, TravelStep step, CancellationToken ct)
    {
        ValidateTravelApproach(entry, step, ct);
        var state = Snapshot;
        var npc = FindTravelNpc(state, step.NpcTransfer!);
        if (npc is null) return null;
        if (TravelNpcVisible(step.SourceMap, npc.Name)) return npc;
        Tile goal;
        try
        {
            goal = NpcApproach.Find(maps.Load(step.SourceMap), state.Position!, npc.Position, TravelApproachObstacles(state, step));
        }
        catch (QuestBlockedException)
        {
            // An early draw can disagree with the recorded stop. Reach the safe
            // interior approach before treating that coordinate as authoritative.
            return null;
        }
        await WalkTravelApproach(goal, entry, step, ct);
        ValidateTravelApproach(entry, step, ct);
        return TravelNpcVisible(step.SourceMap, npc.Name) ? FindTravelNpc(Snapshot, step.NpcTransfer!) : null;
    }

    async Task<Entity> ApproachTravelNpc(TravelStep step, CharacterSnapshot entry, CancellationToken ct)
    {
        var transfer = step.NpcTransfer!;
        ValidateTravelApproach(entry, step, ct);
        if (TravelNpcVisible(step.SourceMap, transfer.Npc))
            return FindTravelNpc(Snapshot, transfer)!;
        for (int range = 3; range >= 1; range--)
        {
            ValidateTravelApproach(entry, step, ct);
            var state = Snapshot;
            var blocked = TravelApproachObstacles(state, step);
            Tile goal;
            try
            {
                // Use the route planner's validated floor before solving against
                // an NPC that may not have been drawn correctly at the doorway.
                goal = range == 3 ? step.From : NpcApproach.Find(maps.Load(step.SourceMap), state.Position!, transfer.Anchor, blocked, range);
                if (blocked.Contains(goal) || maps.Load(step.SourceMap).Blocked(goal))
                    goal = NpcApproach.Find(maps.Load(step.SourceMap), state.Position!, transfer.Anchor, blocked, range);
                await WalkTravelApproach(goal, entry, step, ct);
            }
            catch (QuestBlockedException error) when (TravelApproachStable(entry, Snapshot, step.SourceMap))
            {
                // A valid range-three floor may be the nearest public side of a
                // counter. Failure to get closer must still allow a delayed draw.
                if (range < 3) break;
                throw TravelApproachFailure(error.Message, entry, step);
            }
            if (await ApproachObservedTravelNpc(entry, step, ct) is { } npc) return npc;
        }
        var waiting = Snapshot;
        var waitingNpc = FindTravelNpc(waiting, transfer);
        try
        {
            await WaitAsync(state => !TravelApproachStable(entry, state, step.SourceMap) ||
                FindTravelNpc(state, transfer) is { } observed && (waitingNpc is null ||
                    observed.Id != waitingNpc.Id || observed.Position != waitingNpc.Position),
                waiting.Revision, TimeSpan.FromSeconds(3), ct);
        }
        catch (TimeoutException) { }
        if (await ApproachObservedTravelNpc(entry, step, ct) is { } revealed) return revealed;
        throw TravelApproachFailure("NPC was not observed within reachable interaction range after bounded interior discovery; no NPC opened.", entry, step);
    }
}
