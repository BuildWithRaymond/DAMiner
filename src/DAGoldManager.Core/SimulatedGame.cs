namespace DAGoldManager.Core;

// Fictional characters only. The preview host cannot construct a live game connection.
/// <summary>In-memory game gateway for fictional rosters; never launches a client or uses credentials.</summary>
public sealed class SimulatedGame : IGameGateway
{
    readonly Dictionary<string, CharacterState> states = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();
    public IReadOnlyList<CharacterState> States { get { lock (gate) return states.Values.ToArray(); } }

    public SimulatedGame(IEnumerable<SavedCharacter> roster)
    {
        foreach (var character in roster) states[character.Name] = new(character.Name, 99_999_999, 0, 59, 167, 10, 10);
    }
    public Task LaunchAsync(SavedCharacter character, CancellationToken cancellation) => Tick(cancellation);
    public Task<CharacterState> ReadAsync(string name, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(states[name]);
    }
    public Task WalkToBankAsync(string name, CancellationToken cancellation) => Move(name, 167, 10, 10, cancellation);
    public Task ApproachCharacterAsync(string sender, string receiver, CancellationToken cancellation) => Tick(cancellation);
    public Task WalkToRecipientAsync(string courier, RunSettings settings, CancellationToken cancellation)
    {
        if (settings.BankerMode == BankerMode.Local)
        {
            lock (gate) { var banker = states[settings.Recipient]; return Move(courier, banker.Map, Math.Max(0, banker.X - 1), banker.Y, cancellation); }
        }
        return Move(courier, settings.RecipientMap, Math.Max(0, settings.RecipientX - 1), settings.RecipientY, cancellation);
    }
    public Task PrepareBankerAsync(string name, RunSettings settings, CancellationToken cancellation) =>
        Move(name, AbelBank.Map, AbelBank.FloorX, AbelBank.FloorY, cancellation);
    public async Task DepositBarAsync(string name, RunSettings settings, CancellationToken cancellation)
    {
        await Tick(cancellation);
        lock (gate)
        {
            var banker = states[name];
            if (banker.Bars < 1) throw new InvalidOperationException("No simulated bars to deposit.");
            states[name] = banker with { Bars = banker.Bars - 1, BarCapacity = banker.BarCapacity + 1 };
        }
    }
    public async Task CraftBarAsync(string name, CancellationToken cancellation)
    {
        await Tick(cancellation);
        lock (gate)
        {
            var state = states[name];
            if (state.Gold < GoldPlanner.DefaultBarCost || state.BarCapacity <= 0) throw new InvalidOperationException("Simulated craft cannot proceed.");
            states[name] = state with { Gold = state.Gold - GoldPlanner.DefaultBarCost, Bars = state.Bars + 1, BarCapacity = state.BarCapacity - 1 };
        }
    }
    public async Task TransferGoldAsync(string sender, string receiver, long amount, CancellationToken cancellation)
    {
        await Tick(cancellation);
        lock (gate)
        {
            states[sender] = states[sender] with { Gold = states[sender].Gold - amount };
            states[receiver] = states[receiver] with { Gold = states[receiver].Gold + amount };
        }
    }
    public async Task TransferBarsAsync(string sender, string receiver, int amount, CancellationToken cancellation)
    {
        await Tick(cancellation);
        lock (gate)
        {
            var donor = states[sender];
            states[sender] = donor with { Bars = donor.Bars - amount, BarCapacity = donor.BarCapacity + amount };
            if (states.TryGetValue(receiver, out var recipient))
                states[receiver] = recipient with { Bars = recipient.Bars + amount, BarCapacity = recipient.BarCapacity - amount };
        }
    }
    async Task Move(string name, int map, int x, int y, CancellationToken cancellation)
    {
        await Tick(cancellation);
        lock (gate) states[name] = states[name] with { Map = map, X = x, Y = y };
    }
    static Task Tick(CancellationToken cancellation) => Task.Delay(180, cancellation);
}

/// <summary>Accelerates simulated waits so preview runs do not take real deposit intervals.</summary>
public sealed class PreviewClock : IRunClock
{
    public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;
    public async Task DelayAsync(TimeSpan duration, CancellationToken cancellation)
    {
        await Task.Delay(600, cancellation);
        Now += duration;
    }
}
