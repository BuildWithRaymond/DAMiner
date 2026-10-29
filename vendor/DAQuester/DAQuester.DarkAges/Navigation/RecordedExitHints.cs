using System.Text.Json;
using DAQuester.Core;

namespace DAQuester.DarkAges.Navigation;

public partial class MapRepository
{
    sealed record ExitHint(int SourceMap, int DestinationMap, Tile From, bool WorldMap = false);
    bool hintsLoaded;
    readonly Dictionary<(int Destination, bool NpcTransfers), Dictionary<int, int>> topologyDistances = [];

    static bool Inside(MapRecord map, Tile? tile) => tile is not null &&
        tile.X >= 0 && tile.Y >= 0 && tile.X < map.Width && tile.Y < map.Height;

    static Dictionary<int, MapRecord> ReadRecords(string path)
    {
        var entries = JsonSerializer.Deserialize<MapRecord[]>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Route catalog is empty.");
        Dictionary<int, MapRecord> result = [];
        foreach (var entry in entries)
        {
            if (entry is null || entry.Id < 0 || entry.Width <= 0 || entry.Height <= 0 ||
                (long)entry.Width * entry.Height > int.MaxValue || entry.Portals is null ||
                entry.InternalTransfers is null || entry.NpcTransfers is null || !result.TryAdd(entry.Id, entry))
                throw new InvalidDataException("Route catalog has invalid dimensions or duplicate map IDs.");
        }
        foreach (var entry in entries)
        {
            // Old catalogs can contain incomplete/outdated edges. They remain
            // visible in the coverage report, but never become walking targets.
            var portals = entry.Portals.Where(p => p is not null && Inside(entry, p.From) &&
                result.TryGetValue(p.Map, out var destination) && Inside(destination, p.To)).Distinct().ToArray();
            var transfers = entry.InternalTransfers.Where(t => t is not null && Inside(entry, t.From) && Inside(entry, t.To)).Distinct().ToArray();
            var npcTransfers = entry.NpcTransfers.Where(t => t is not null && Inside(entry, t.Anchor) &&
                result.TryGetValue(t.Map, out var destination) && Inside(destination, t.PlanningArrival) &&
                !string.IsNullOrWhiteSpace(t.Npc) && !string.IsNullOrWhiteSpace(t.Service) &&
                t.Destinations is { Length: > 0 } && t.Destinations.All(value => !string.IsNullOrWhiteSpace(value))).Distinct().ToArray();
            result[entry.Id] = entry with { Portals = portals, InternalTransfers = transfers, NpcTransfers = npcTransfers };
        }
        return result;
    }

    void EnsureExitHints()
    {
        if (hintsLoaded) return;
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(routeFile))!, "route-hints.json");
        var hints = File.Exists(path) ? JsonSerializer.Deserialize<ExitHint[]>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Exit hint catalog is empty.") : [];
        if (hints.Any(h => h is null || h.From is null))
            throw new InvalidDataException("Exit hint catalog contains an invalid entry.");
        foreach (var group in hints.GroupBy(h => h.SourceMap))
        {
            if (!records.TryGetValue(group.Key, out var source)) continue;
            var accepted = new List<Portal>();
            foreach (var hint in group.Distinct())
            {
                if (hint.DestinationMap == source.Id || !Inside(source, hint.From) ||
                    !records.ContainsKey(hint.DestinationMap) || source.InternalTransfers.Any(t => t.From == hint.From) ||
                    source.NpcTransfers.Any(transfer => transfer.Map == hint.DestinationMap)) continue;
                var existing = source.Portals.Where(p => p.From == hint.From).ToArray();
                bool world = existing.Any(p => p.WorldMap);
                if (existing.Any(p => !p.WorldMap && p.Map != hint.DestinationMap) ||
                    hint.WorldMap && !world || !world && group.Any(h => h.From == hint.From && h.DestinationMap != hint.DestinationMap)) continue;
                if (existing.Any(p => p.Map == hint.DestinationMap)) continue;
                accepted.Add(new(hint.From, hint.DestinationMap, null, world));
            }
            records[source.Id] = source with { Portals = [.. source.Portals, .. accepted.Distinct()] };
        }
        // All geometry/reachability caches belong to this immutable session catalog.
        destinationReachability.Clear();
        topologyDistances.Clear();
        distanceFields.Clear();
        fieldOrder.Clear();
        cachedCells = 0;
        hintsLoaded = true;
    }

    Dictionary<int, int> HopDistances(int destination, bool includeNpcTransfers)
    {
        var key = (destination, includeNpcTransfers);
        if (topologyDistances.TryGetValue(key, out var saved)) return saved;
        var reverse = records.Values.SelectMany(r => (includeNpcTransfers ? r.Connections : r.Portals)
            .Select(p => (To: p.Map, From: r.Id))).ToLookup(p => p.To, p => p.From);
        var distances = new Dictionary<int, int> { [destination] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(destination);
        while (queue.TryDequeue(out int map))
            foreach (int parent in reverse[map])
                if (distances.TryAdd(parent, distances[map] + 1)) queue.Enqueue(parent);
        if (topologyDistances.Count >= 16) topologyDistances.Clear();
        topologyDistances[key] = distances;
        return distances;
    }

    TravelPlan? NextRecordedExit(int from, Tile origin, int destination, ISet<Tile>? obstacles, ISet<int> unavailableCollision, CancellationToken ct, bool includeNpcTransfers)
    {
        var hops = HopDistances(destination, includeNpcTransfers);
        if (!hops.TryGetValue(from, out int remaining) || remaining == 0) return null;
        var source = Record(from);
        var field = WalkingDistances(from, origin, obstacles, ct);
        var choices = new List<(Portal Exit, int Walk, int Hops)>();
        foreach (var portal in includeNpcTransfers ? source.Connections : source.Portals)
        {
            ct.ThrowIfCancellationRequested();
            // Strictly reducing map distance prevents oscillation between hints.
            if (!hops.TryGetValue(portal.Map, out int tail) || tail >= remaining) continue;
            var approach = portal.NpcTransfer is { } transfer ? TravelNpcApproach(source, origin, transfer, obstacles, field) : portal.From;
            if (approach is null) continue;
            int walk = portal.NpcTransfer is not null ? Distance(source, field, approach) + 5 : TriggerDistance(source, field, origin, portal.From, obstacles);
            if (walk < 0) continue;
            try { Load(portal.Map); }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException or InvalidOperationException)
            {
                unavailableCollision.Add(portal.Map);
                continue;
            }
            choices.Add((portal with { From = approach }, walk, tail));
        }
        var choice = choices.OrderBy(c => c.Hops).ThenBy(c => c.Walk).FirstOrDefault();
        if (choice.Exit is null) return null;
        return new([new(from, choice.Exit.From, choice.Exit.Map, choice.Exit.To, choice.Exit.WorldMap, choice.Exit.NpcTransfer)], choice.Walk + choice.Hops)
            { RequiresReplan = true };
    }
}
