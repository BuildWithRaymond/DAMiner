using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using DAGoldManager.Core;
using DAQuester.Core;
using DAQuester.DarkAges;
using DAQuester.DarkAges.Launcher;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Transport;

namespace DAGoldManager.Game;

// Launch and navigation use DAQuester's supported client and confirmed walker.
// This class never attaches to, closes, or restarts another application's clients.
public sealed partial class GameGateway : IGameGateway
{
    readonly ProxyHost host = new();
    readonly ConcurrentDictionary<string, CharacterConnection> characters = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, Process> clients = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, bool> fastModes = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, int> craftBanks = new(StringComparer.OrdinalIgnoreCase);
    readonly object craftBankSelection = new();
    readonly object fastModeLock = new();
    readonly CharacterCommandLocks commandLocks = new();
    readonly SemaphoreSlim loginInput = new(1, 1);
    readonly Func<string, string> readPassword;
    readonly IOperationLedger? travelLedger;
    readonly string contentDirectory;
    readonly CraftCooldowns cooldowns = new();
    GameSettings settings;
    CraftRecipe? recipe;
    string externalRecipient = "";

    public event Action<string>? Diagnostic;
    public event Action<string>? CharacterChanged;
    public SetupRecorder? Recorder { get; private set; }
    ManualRecording? recording;
    string recordingCharacter = "";
    public string? RecordingError => recording?.Error;

    public void SeedCooldowns(IEnumerable<OperationRecord> operations)
    {
        foreach (var operation in operations.Where(o => o.Kind == "craft" && o.Outcome == OperationOutcome.Verified))
            cooldowns.RecordCraft(operation.Sender, operation.Time);
    }

    public IReadOnlyList<CharacterState> KnownStates()
    {
        var result = new List<CharacterState>();
        foreach (var character in characters.Values)
        {
            var state = character.Snapshot;
            if (!state.Connected || !state.InventoryKnown || state.Gold is null || state.Map is null || state.Position is null || state.Weight is null || state.MaxWeight is null) continue;
            int freeSlots = 59 - state.Inventory.Count;
            int spareWeight = Math.Max(0, state.MaxWeight.Value - state.Weight.Value);
            int capacity = settings.BarWeight <= 0 ? freeSlots : Math.Min(freeSlots, spareWeight / settings.BarWeight);
            result.Add(new(state.Name!, state.Gold.Value, state.Count(recipe?.BarName ?? "Gold Bar"), capacity,
                state.Map.Value, state.Position.X, state.Position.Y, state.Registered)
                { CraftBankMap = CraftBankFor(character) });
        }
        return result;
    }

    public GameGateway(GameSettings settings, CraftRecipe? recipe, Func<string, string> readPassword, string contentDirectory,
        IOperationLedger? travelLedger = null)
    {
        this.settings = settings;
        this.recipe = recipe;
        this.readPassword = readPassword;
        this.travelLedger = travelLedger;
        this.contentDirectory = contentDirectory;
        host.Diagnostic += message => Diagnostic?.Invoke(message);
    }

    public void Configure(GameSettings nextSettings, CraftRecipe? nextRecipe, string recipient = "")
    {
        if (characters.Values.Any(c => c.Snapshot.Connected) && settings != nextSettings)
            throw new InvalidOperationException("Game connection settings cannot change while managed characters are connected.");
        settings = nextSettings;
        recipe = nextRecipe;
        externalRecipient = recipient;
    }

    public void SetFastMode(string name, bool enabled)
    {
        lock (fastModeLock)
        {
            fastModes[name] = enabled;
            if (characters.TryGetValue(name, out var character)) character.Session.FastMode = enabled;
        }
    }

    public string FastModeDescription(string name) => characters.TryGetValue(name, out var character) && character.Snapshot.Connected
        ? character.Session.FastModeDescription : "ready when connected";

    bool IsConnectedOwnedCharacter(string name) => characters.TryGetValue(name, out var character) && character.Snapshot.Connected;

    public async Task LaunchAsync(SavedCharacter character, CancellationToken cancellation)
    {
        fastModes.TryAdd(character.Name, character.FastMode);
        if (character.Name.Equals(externalRecipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The external banker is already logged in and cannot be launched locally.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Live characters require Windows.");
        if (await ResumePendingTravelAsync(character.Name, cancellation)) return;
        if (characters.TryGetValue(character.Name, out var current) && current.Snapshot.Connected) return;
        if (clients.TryGetValue(character.Name, out var existing) && !existing.HasExited)
            throw new InvalidOperationException($"{character.Name}'s client is already open but not ready. Check its login before proceeding.");

        ClientLauncher.Validate(settings.ClientPath);
        craftBanks.TryRemove(character.Name, out _);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var ticket = new LoginTicketLifetime(new SavedLoginTicket(character.Name, () => readPassword(character.Name)));
        var connected = new TaskCompletionSource<CharacterConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Only native login input needs shared desktop focus. Release before waiting for game readiness.
        await loginInput.WaitAsync(cancellation);
        try
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            int port = await host.StartOwnedAsync(new(IPAddress.Parse(settings.ServerAddress), settings.ServerPort), ticket.Value,
                connection => connection.Authenticated += authenticated =>
                {
                    string? actual = authenticated.Tracker.Snapshot.Name;
                    if (!character.Name.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    {
                        connected.TrySetException(new InvalidOperationException("Login connected as a different character."));
                        return;
                    }
                    var maps = new MapRepository(Path.GetDirectoryName(settings.ClientPath)!, Path.Combine(contentDirectory, "routes.json"), settings.ServerProfile);
                    var managed = new CharacterConnection(authenticated, maps, settings.MovementMilliseconds,
                        fastModes[character.Name], IsConnectedOwnedCharacter);
                    lock (fastModeLock)
                    {
                        characters[character.Name] = managed;
                        // A checkbox can change while authentication constructs
                        // the session. Apply the latest preference under the same lock.
                        managed.Session.FastMode = fastModes[character.Name];
                    }
                    ObserveTravelConnection(managed);
                    managed.Session.Changed += _ => CharacterChanged?.Invoke(character.Name);
                    connected.TrySetResult(managed);
                }, timeout.Token);

            int pid = await Task.Run(() => ClientLauncher.Launch(settings.ClientPath, port), timeout.Token);
            var process = Process.GetProcessById(pid);
            clients[character.Name] = process;
            while (process.MainWindowHandle == IntPtr.Zero)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("Game client closed before sign-in.");
                await Task.Delay(50, timeout.Token);
                process.Refresh();
            }
            using var form = new NativeLoginForm(pid);
            await LoginFormAutomation.Run(form, character.Name, ticket.Value.Ticket,
                message => Diagnostic?.Invoke(character.Name + ": " + message), timeout.Token);
        }
        finally { loginInput.Release(); }
        var ready = await connected.Task.WaitAsync(timeout.Token);
        await WaitForReady(ready, timeout.Token);
    }

    public async Task<CharacterState> ReadAsync(string name, CancellationToken cancellation)
    {
        var character = GetCharacter(name);
        await WaitForReady(character, cancellation);
        var state = character.Snapshot;
        int freeSlots = 59 - state.Inventory.Count;
        int spareWeight = Math.Max(0, state.MaxWeight!.Value - state.Weight!.Value);
        int capacity = settings.BarWeight <= 0 ? freeSlots : Math.Min(freeSlots, spareWeight / settings.BarWeight);
        return new(name, state.Gold!.Value, state.Count(recipe?.BarName ?? "Gold Bar"), capacity,
            state.Map!.Value, state.Position!.X, state.Position.Y, state.Registered)
            { CraftBankMap = CraftBankFor(character) };
    }

    static async Task WaitForReady(CharacterConnection character, CancellationToken cancellation)
    {
        bool Ready(CharacterSnapshot s) => s.Connected && s.Name is not null && s.Id is not null &&
            s.Gold is not null && s.InventoryKnown && s.Map is not null && s.Position is not null && s.Weight is not null && s.MaxWeight is not null;
        if (!Ready(character.Snapshot))
            await character.Session.WaitAsync(Ready, character.Snapshot.Revision, TimeSpan.FromSeconds(20), cancellation);
        if (character.Snapshot.Gold > GoldPlanner.CarryingLimit)
            throw new InvalidOperationException("Observed gold exceeds the configured carrying cap.");
    }

    public Task WalkToBankAsync(string name, CancellationToken cancellation)
    {
        var character = GetCharacter(name);
        var learned = CraftRecipeFor(character);
        return Control([character], async token =>
        {
            await NavigateManagedAsync(name, BankNavigation.Arrival(learned), token);
        }, cancellation);
    }

    public Task ApproachCharacterAsync(string sender, string receiver, CancellationToken cancellation)
    {
        var target = GetCharacter(receiver);
        return Control([GetCharacter(sender), target], async token =>
        {
            var state = GetCharacter(receiver).Snapshot;
            await NavigateManagedAsync(sender, new("navigate", state.Map, state.Position, EntityId: state.Id, Tolerance: 2)
                { StopWithinTolerance = true }, token);
            RequirePlayer(GetCharacter(sender), receiver);
        }, cancellation);
    }

    public Task WalkToRecipientAsync(string courier, RunSettings run, CancellationToken cancellation)
    {
        if (run.BankerMode == BankerMode.Local)
            return ApproachCharacterAsync(courier, run.Recipient, cancellation);
        var character = GetCharacter(courier);
        return Control([character], async token =>
        {
            bool Visible(CharacterSnapshot state) => state.Map == run.RecipientMap &&
                state.Entities.Values.Any(e => e.IsPlayer && e.Name.Equals(run.Recipient, StringComparison.OrdinalIgnoreCase));
            if (!Visible(character.Snapshot))
                character = await NavigateManagedAsync(courier, new("navigate", run.RecipientMap, new(run.RecipientX, run.RecipientY), Tolerance: 3)
                    { StopWithinTolerance = true }, token);
            if (!Visible(character.Snapshot))
                await character.Session.WaitAsync(s => s.Entities.Values.Any(e => e.IsPlayer && e.Name.Equals(run.Recipient, StringComparison.OrdinalIgnoreCase)),
                    character.Snapshot.Revision, TimeSpan.FromSeconds(30), token);
            var target = RequirePlayer(character, run.Recipient, checkRange: false);
            character = await NavigateManagedAsync(courier, new("navigate", run.RecipientMap, target.Position, EntityId: target.Id, Tolerance: 2)
                { StopWithinTolerance = true }, token);
            RequirePlayer(character, run.Recipient);
        }, cancellation);
    }

    public void BeginRecording(string name, CraftRecipe? savedRecipe = null, int verifiedBatchSize = 0)
    {
        var character = GetCharacter(name);
        recording?.Dispose();
        // Restart observation without asking the user to pay for the same demonstration craft again.
        savedRecipe ??= Recorder?.Recipe ?? recipe;
        Recorder = new SetupRecorder(savedRecipe, verifiedBatchSize);
        recordingCharacter = name;
        recording = new ManualRecording(character, Recorder, () => CharacterChanged?.Invoke(name));
    }

    public void EndRecording()
    {
        if (Recorder?.Recipe is { } learned && recordingCharacter.Length > 0)
            cooldowns.RecordCraft(recordingCharacter, learned.RecordedAt);
        recording?.Dispose();
        recording = null;
    }

    CharacterConnection GetCharacter(string name)
    {
        if (!characters.TryGetValue(name, out var character) || !character.Snapshot.Connected)
            throw new InvalidOperationException($"{name} is not connected through this manager.");
        return character;
    }

    CraftRecipe RequireRecipe() => recipe ?? throw new InvalidOperationException("Record and save bank setup first.");

    int CraftBankFor(CharacterConnection character)
    {
        var state = character.Snapshot;
        // UI snapshots and the run can inspect the first login together. Loading
        // route hints during that classification must happen once before navigation.
        lock (craftBankSelection)
            return craftBanks.GetOrAdd(state.Name ?? throw new InvalidOperationException("Character identity missing."), _ =>
                character.Maps.IsMedenia(state.Map ?? throw new InvalidOperationException("Waiting for login map."))
                    ? RegionalCrafting.NoamMap : AbelBank.Map);
    }

    CraftRecipe CraftRecipeFor(CharacterConnection character) => RegionalCrafting.ForBank(RequireRecipe(), CraftBankFor(character));

    static Entity RequirePlayer(CharacterConnection character, string name, bool checkRange = true)
    {
        var candidates = character.Snapshot.Entities.Values.Where(e => e.IsPlayer && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException($"Cannot identify visible player {name} uniquely.");
        var target = candidates[0];
        if (checkRange && (character.Snapshot.Position is not { } position ||
            Math.Abs(position.X - target.Position.X) + Math.Abs(position.Y - target.Position.Y) > 2))
            throw new InvalidOperationException($"{name} moved out of trade range. No offer was sent.");
        return target;
    }

    async Task Control(CharacterConnection[] participants, Func<CancellationToken, Task> action, CancellationToken cancellation)
    {
        await commandLocks.RunAsync(participants.Select(c => c.Snapshot.Name ?? throw new InvalidOperationException("Character identity missing.")), async () =>
        {
            using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            string? reason = null;
            void Stop(string message) { reason = message; interrupted.Cancel(); }
            var participantNames = participants.Select(character => character.Snapshot.Name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            void OnManagedInterruption(CharacterConnection character, string message)
            {
                if (character.Snapshot.Name is { } name && participantNames.Contains(name) &&
                    !TryAcceptTravelInterruption(character, message)) Stop(message);
            }
            foreach (var character in participants) ObserveTravelConnection(character);
            ManagedCharacterInterrupted += OnManagedInterruption;
            try { await action(interrupted.Token); }
            catch (OperationCanceledException error) when (!cancellation.IsCancellationRequested && reason is not null)
            {
                string detail = error is TradeCancellationException ? $"{error.Message} Interruption: {reason}" : reason;
                throw new InvalidOperationException(detail, error);
            }
            finally
            {
                ManagedCharacterInterrupted -= OnManagedInterruption;
            }
        }, cancellation);
    }

    sealed class LoginTicketLifetime(SavedLoginTicket value) : IDisposable
    {
        public SavedLoginTicket Value => value;
        public void Dispose() => value.Cancel();
    }
}

/// <summary>Binds one authenticated connection, map repository, and acknowledged command session.</summary>
public sealed class CharacterConnection
{
    public ProxyConnection Connection { get; }
    public GameSession Session { get; }
    public MapRepository Maps { get; }
    public TradeWindowState TradeWindow { get; } = new();
    public CharacterSnapshot Snapshot => Session.Snapshot;

    public CharacterConnection(ProxyConnection connection, MapRepository maps, int cadence,
        bool fastMode = false, Func<string, bool>? isOwnCharacter = null)
    {
        Connection = connection;
        Maps = maps;
        Session = new(connection, maps, cadence) { FastMode = fastMode, IsOwnCharacter = isOwnCharacter };
        connection.Tracker.Observed += (opcode, payload) =>
        {
            if (opcode is not (0x42 or 0x0A)) return;
            try
            {
                if (opcode == 0x42) TradeWindow.Observe(ExchangeReader.Read(payload));
                else if (TradeWindow.IsOpen &&
                    ServerMessageFactory.Default.Create(new ServerPacket(opcode, payload)) is ServerSystemMessage message &&
                    TradePopupCleanup.IsServerCompletion((byte)message.MessageType, message.Message))
                    TradeWindow.ObserveCompletion(message.Message);
            }
            catch (Exception)
            {
                // Keep the attempt's own listener alive so it can report the
                // decoding failure. An unknown window cannot permit a new trade.
                TradeWindow.MarkUncertain();
            }
        };
    }
}
