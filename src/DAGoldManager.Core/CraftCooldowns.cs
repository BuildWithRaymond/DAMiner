namespace DAGoldManager.Core;

/// <summary>Tracks verified purchases per character so independent crafters respect their own cooldowns.</summary>
public sealed class CraftCooldowns
{
    readonly Dictionary<string, DateTimeOffset> lastCrafts = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();
    public void RecordCraft(string name, DateTimeOffset time)
    {
        lock (gate)
        {
            if (!lastCrafts.TryGetValue(name, out var previous) || time > previous) lastCrafts[name] = time;
        }
    }
    public TimeSpan Remaining(string name, DateTimeOffset now, int cooldownSeconds)
    {
        if (cooldownSeconds < 0) throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
        lock (gate)
        {
            if (!lastCrafts.TryGetValue(name, out var previous)) return TimeSpan.Zero;
            var remaining = previous.AddSeconds(cooldownSeconds) - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}
