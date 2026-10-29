namespace DAQuester.Core;

public static class NpcVisibility
{
    public const int ScreenRange = 10;

    public static Entity? Find(CharacterSnapshot snapshot, int map, string name)
    {
        if (!snapshot.Connected || snapshot.Map != map || snapshot.Position is not { } position)
            return null;
        // Match DAQuester's screen visibility, not the walking approach radius.
        // Mundanes behind counters can be usable without a three-tile floor path.
        var matches = snapshot.Entities.Values.Where(entity => !entity.IsPlayer &&
            entity.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 && Math.Abs(matches[0].Position.X - position.X) +
            Math.Abs(matches[0].Position.Y - position.Y) <= ScreenRange ? matches[0] : null;
    }
}
