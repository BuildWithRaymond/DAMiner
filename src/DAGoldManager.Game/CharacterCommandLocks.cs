using System.Collections.Concurrent;

namespace DAGoldManager.Game;

/// <summary>Locks affected characters in a stable order, allowing unrelated workers to continue.</summary>
public sealed class CharacterCommandLocks
{
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task RunAsync(IEnumerable<string> characters, Func<Task> action, CancellationToken cancellation)
    {
        var acquired = new List<SemaphoreSlim>();
        try
        {
            // A trade always acquires both participants in the same order, avoiding deadlocks.
            foreach (var name in characters.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                var gate = gates.GetOrAdd(name, _ => new(1, 1));
                await gate.WaitAsync(cancellation);
                acquired.Add(gate);
            }
            await action();
        }
        finally
        {
            for (int index = acquired.Count - 1; index >= 0; index--) acquired[index].Release();
        }
    }
}
