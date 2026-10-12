using DAGoldManager.Core;
using DAGoldManager.Game;
using DAGoldManager.Storage;

namespace DAGoldManager.UI;

/// <summary>Owns roster editing, run/recording lifecycle, and state displayed by shared Razor components.</summary>
/// <remarks>Demo uses the same coordinator with a simulator, never a live gateway.</remarks>
public sealed class DashboardModel
{
    readonly LocalStore store;
    readonly PasswordVault passwords;
    readonly string contentDirectory;
    GameGateway? liveGame;
    SimulatedGame? simulatedGame;
    RunCoordinator? coordinator;
    CancellationTokenSource? cancellation;
    AppConfiguration configuration;
    readonly List<RunProgress> activity = [];
    readonly Dictionary<string, RunProgress> characterProgress = new(StringComparer.OrdinalIgnoreCase);
    readonly object activityLock = new();
    readonly object recordingLock = new();
    // Gateway callbacks may come from worker threads. Components marshal Changed onto their UI context.
    public event Action? Changed;
    public bool Demo { get; }
    public bool Busy { get; private set; }
    public bool Paused { get; private set; }
    public bool Recording { get; private set; }
    public string Message { get; private set; } = "Save your roster, record bank setup, then press Go.";
    public string? Error { get; private set; }
    public RunProgress Progress { get; private set; } = new(RunPhase.Idle, "", "Ready for setup");
    public RunResult? Result { get; private set; }
    public SavedCharacter[] Characters => configuration.Characters;
    public SavedCharacter[] Producers => Characters.Where(c => !c.Name.Equals(Recipient, StringComparison.OrdinalIgnoreCase)).ToArray();
    public CraftRecipe? Recipe => configuration.Recipe;
    public IReadOnlyList<RunProgress> Activity { get { lock (activityLock) return activity.AsEnumerable().Reverse().Take(30).ToArray(); } }
    public IReadOnlyList<OperationRecord> History => store.RecentOperations();
    public bool NeedsReview => store.HasUnresolvedOperations();
    public IReadOnlyList<string> RecordingEvents => liveGame?.Recorder?.Events ?? [];
    public CraftRecipe? RecordedRecipe => configuration.Draft?.Recipe;
    public int RecordedBatchSize => configuration.Draft?.VerifiedBatchSize ?? (Recipe is null ? 0 : BatchSize);
    public bool CanSaveRecording => !Busy && configuration.Draft is { VerifiedBatchSize: > 0, Error: null };
    public string RecordingButtonText => RecordedRecipe is not null || Recipe is not null ? "Resume recording" : "Launch and record";
    public string RecordingStatus
    {
        get
        {
            if (Busy && !Recording) return "Starting recording…";
            if (RecordingError.Length > 0) return "Recording needs attention. Resume after checking the error below.";
            string state = Recording ? "Recording active." : "Recording off.";
            if (CanSaveRecording) return state + " Craft and sample trade verified. Ready to save.";
            if (configuration.Draft is null && Recipe is not null) return state +
                (BankerConfigured ? " Setup saved. Go is ready." : " Bank setup saved. Complete banker settings before Go.");
            if (RecordedRecipe is null) return state + " Make one bar, then complete a sample bar trade.";
            return state + (Recording ? " Craft saved automatically. Complete a sample bar trade." :
                " Craft saved automatically. Resume recording to capture the sample trade.");
        }
    }
    public string Courier { get; set; }
    public string Recipient { get; set; }
    public BankerMode BankerMode { get; set; }
    public string DepositNpcName { get; set; }
    public int RecipientMap { get; set; }
    public int RecipientX { get; set; }
    public int RecipientY { get; set; }
    public bool BankerConfigured => Enum.IsDefined(BankerMode) && Recipient.Length is >= 1 and <= 16 && Recipient.All(char.IsAsciiLetter) &&
        Characters.Any(c => c.Enabled && c.Name.Equals(Courier, StringComparison.OrdinalIgnoreCase)) &&
        !Recipient.Equals(Courier, StringComparison.OrdinalIgnoreCase) &&
        (BankerMode == BankerMode.External ? RecipientMap > 0 :
            Characters.Any(c => c.Enabled && c.Name.Equals(Recipient, StringComparison.OrdinalIgnoreCase)));
    public string BankerDestination
    {
        get
        {
            if (BankerMode == BankerMode.External) return RecipientMap > 0 ? $"Map {RecipientMap} · {RecipientX},{RecipientY}" : "Choose destination in Settings";
            return "Abel bank · Lamont";
        }
    }
    public int BatchSize { get; set; }
    public int DepositWaitSeconds { get; set; }
    public bool PoolGold { get; set; }
    public string ClientPath { get; set; }
    public string ServerAddress { get; set; }
    public int ServerPort { get; set; }
    public int BarWeight { get; set; }
    public string RecordingCharacter { get; set; } = "";
    public string RecordingError => liveGame?.RecordingError ?? configuration.Draft?.Error ?? "";

    public DashboardModel(bool demo, string dataDirectory, string contentDirectory)
    {
        Demo = demo;
        this.contentDirectory = contentDirectory;
        store = new(dataDirectory);
        passwords = new(dataDirectory);
        configuration = demo ? DemoConfiguration() : store.Load();
        RecordingCharacter = configuration.Draft?.Character ?? "";
        Courier = configuration.Run.Courier;
        Recipient = configuration.Run.Recipient;
        BankerMode = configuration.Run.BankerMode;
        DepositNpcName = configuration.Run.DepositNpcName;
        RecipientMap = configuration.Run.RecipientMap;
        RecipientX = configuration.Run.RecipientX;
        RecipientY = configuration.Run.RecipientY;
        BatchSize = configuration.Run.BarsPerTrade;
        DepositWaitSeconds = configuration.Run.DepositWaitSeconds;
        PoolGold = configuration.Run.PoolLeftoverGold;
        ClientPath = configuration.Game.ClientPath;
        ServerAddress = configuration.Game.ServerAddress;
        ServerPort = configuration.Game.ServerPort;
        BarWeight = configuration.Game.BarWeight;
        if (configuration.Recipe is not null)
            Progress = new(RunPhase.Idle, "", "Enabled characters craft in parallel, then one courier delivers.");
        if (demo)
        {
            simulatedGame = new(configuration.Characters);
            Message = "Simulated characters. No game clients, accounts, or passwords are used.";
        }
        else if (configuration.Draft is not null) Message = "Verified recording progress restored. Finish or save it in Record setup.";
        else if (configuration.Recipe is not null) Message = "Bank setup saved. Choose your courier and press Go.";
    }

    public bool PasswordSaved(string name) => Demo || passwords.HasPassword(name);

    public void SaveCharacter(string name, string password)
    {
        EnsureIdle();
        name = name.Trim();
        if (name.Length is < 1 or > 16 || !name.All(char.IsAsciiLetter)) throw new InvalidOperationException("Use 1–16 letters for a character name.");
        if (Demo) throw new InvalidOperationException("Passwords and real accounts cannot be saved in preview.");
        if (BankerMode == BankerMode.External && name.Equals(Recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The banker is already logged in independently. Save only producers and your courier here.");
        if (!string.IsNullOrEmpty(password)) passwords.Save(name, password);
        var existing = configuration.Characters.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var roster = configuration.Characters.Where(c => !c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        roster.Add(existing ?? new(name));
        configuration = configuration with { Characters = roster.ToArray() };
        if (Courier.Length == 0 && !name.Equals(Recipient, StringComparison.OrdinalIgnoreCase)) Courier = name;
        SaveSettings();
        Message = passwords.HasPassword(name) ? $"Saved {name}. Password is encrypted by Windows." : $"Saved {name}. Add its password before automatic launch.";
        Notify();
    }

    public void RemoveCharacter(string name)
    {
        EnsureIdle();
        configuration = configuration with { Characters = configuration.Characters.Where(c => c.Name != name).ToArray() };
        if (Courier == name) Courier = configuration.Characters.FirstOrDefault()?.Name ?? "";
        SaveSettings();
        Notify();
    }

    public void SetEnabled(string name, bool enabled)
    {
        EnsureIdle();
        configuration = configuration with { Characters = configuration.Characters.Select(c => c.Name == name ? c with { Enabled = enabled } : c).ToArray() };
        SaveSettings();
    }

    public void SaveSettings()
    {
        if (Busy || Recording) throw new InvalidOperationException("Finish the current operation before changing settings.");
        if (!Enum.IsDefined(BankerMode) || DepositNpcName.Length > 128 || BatchSize is < 1 or > 59 || DepositWaitSeconds is < 0 or > 3600 || BarWeight is < 0 or > 1000 || ServerPort is < 1 or > 65535 ||
            RecipientMap is < 0 or > 65535 || RecipientX is < 0 or > 65535 || RecipientY is < 0 or > 65535 ||
            Recipient.Length > 0 && (Recipient.Trim().Length is < 1 or > 16 || !Recipient.Trim().All(char.IsAsciiLetter)))
            throw new InvalidOperationException("Check batch size, wait time, weight, and server port.");
        configuration = configuration with
        {
            Run = configuration.Run with { Courier = Courier, Recipient = Recipient.Trim(),
                BankerMode = BankerMode, DepositNpcName = DepositNpcName.Trim(),
                RecipientMap = RecipientMap, RecipientX = RecipientX, RecipientY = RecipientY, BarsPerTrade = BatchSize,
                DepositWaitSeconds = DepositWaitSeconds, PoolLeftoverGold = PoolGold, BarCost = Recipe?.GoldCost ?? GoldPlanner.DefaultBarCost },
            Game = configuration.Game with { ClientPath = ClientPath.Trim(), ServerAddress = ServerAddress.Trim(), ServerPort = ServerPort, BarWeight = BarWeight }
        };
        if (!Demo) store.Save(configuration);
        Message = "Settings saved.";
        Notify();
    }

    public void SetCanCraftBars(string name, bool canCraft)
    {
        EnsureIdle();
        configuration = configuration with { Characters = configuration.Characters.Select(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ? c with { CanCraftBars = canCraft } : c).ToArray() };
        SaveSettings();
    }

    GameGateway GetLiveGame()
    {
        if (Demo) throw new InvalidOperationException("Live game connections are disabled in preview.");
        if (liveGame is null)
        {
            liveGame = new(configuration.Game, configuration.Recipe, passwords.Read, contentDirectory);
            liveGame.SeedCooldowns(store.RecentOperations());
            liveGame.CharacterChanged += _ =>
            {
                CaptureRecordingDraft();
                Notify();
            };
        }
        liveGame.Configure(configuration.Game, configuration.Recipe,
            configuration.Run.BankerMode == BankerMode.External ? configuration.Run.Recipient : "");
        return liveGame;
    }

    public async Task StartRecordingAsync()
    {
        EnsureIdle();
        SaveSettings();
        var character = Producers.SingleOrDefault(c => c.Name == RecordingCharacter)
            ?? throw new InvalidOperationException("Choose a saved character to record.");
        if (!PasswordSaved(character.Name)) throw new InvalidOperationException("Save this character's password before launching.");
        Busy = true;
        cancellation = new();
        Notify();
        try
        {
            var game = GetLiveGame();
            var banker = BankerMode == BankerMode.Local ? Characters.FirstOrDefault(c => c.Enabled &&
                c.Name.Equals(Recipient, StringComparison.OrdinalIgnoreCase) && PasswordSaved(c.Name)) : null;
            var launches = new List<Task> { game.LaunchAsync(character, cancellation.Token) };
            if (banker is not null) launches.Add(game.LaunchAsync(banker, cancellation.Token));
            await Task.WhenAll(launches);
            game.BeginRecording(character.Name, configuration.Draft?.Recipe ?? Recipe,
                configuration.Draft?.VerifiedBatchSize ?? 0);
            Recording = true;
            CaptureRecordingDraft();
            Message = RecordedRecipe is null ?
                "In the game, make one bar, then complete a sample bar trade. Verified progress saves automatically." :
                "Craft recipe retained. Complete a sample bar trade, then save setup.";
        }
        finally { Busy = false; Notify(); }
    }

    public void SaveRecording()
    {
        if (!CanSaveRecording) throw new InvalidOperationException("Complete one verified craft and one verified bar trade before saving setup.");
        StopRecording();
        // Stop first so no late observer can overwrite the saved configuration with a draft.
        if (!CanSaveRecording) throw new InvalidOperationException("Recording changed before saving. Check its status and error.");
        var draft = configuration.Draft!;
        BatchSize = draft.VerifiedBatchSize;
        configuration = configuration with { Recipe = draft.Recipe, Draft = null, Run = configuration.Run with
        { BarCost = draft.Recipe.GoldCost, BarsPerTrade = BatchSize } };
        store.Save(configuration);
        liveGame?.Configure(configuration.Game, configuration.Recipe,
            configuration.Run.BankerMode == BankerMode.External ? configuration.Run.Recipient : "");
        Message = "Bank recipe and verified trade batch saved. Go is ready.";
        Notify();
    }

    public void StopRecording()
    {
        lock (recordingLock)
        {
            CaptureRecordingDraft();
            Recording = false;
            liveGame?.EndRecording();
        }
        Notify();
    }

    void CaptureRecordingDraft()
    {
        lock (recordingLock)
        {
            if (!Recording || Demo || liveGame?.Recorder?.Recipe is not { } learned) return;
            var draft = new RecordingDraft(RecordingCharacter, learned,
                liveGame.Recorder.VerifiedBatchSize, liveGame.RecordingError);
            if (configuration.Draft == draft) return;
            configuration = configuration with { Draft = draft };
            // Persist only verified semantic data, never raw packets or passwords.
            store.Save(configuration);
        }
    }

    public async Task GoAsync()
    {
        EnsureIdle();
        SaveSettings();
        if (Recipe is null) throw new InvalidOperationException("Record bank setup first.");
        if (!BankerConfigured) throw new InvalidOperationException("Complete banker setup in Settings: choose a separate enabled local banker and deposit NPC, or an external banker and destination map.");
        if (NeedsReview) throw new InvalidOperationException("Inspect interrupted operations in History and acknowledge them before a fresh run.");
        if (Producers.Any(c => c.Enabled && !PasswordSaved(c.Name))) throw new InvalidOperationException("Save passwords for every enabled producer and courier.");
        if (BankerMode == BankerMode.Local && !PasswordSaved(Recipient)) throw new InvalidOperationException("Save the local banker's password in Characters before starting.");
        Error = null;
        Result = null;
        IGameGateway game;
        if (Demo) { simulatedGame = new(Characters); game = simulatedGame; }
        else game = GetLiveGame();
        Busy = true;
        Paused = false;
        lock (activityLock) characterProgress.Clear();
        cancellation = new();
        coordinator = new(game, store, Demo ? new PreviewClock() : new SystemRunClock());
        coordinator.Changed += progress =>
        {
            if (progress.Phase != RunPhase.WaitingForCollection) Progress = progress;
            lock (activityLock)
            {
                activity.Add(progress);
                if (progress.Character.Length > 0) characterProgress[progress.Character] = progress;
            }
            Notify();
        };
        Notify();
        try { Result = await coordinator.RunAsync(Characters, configuration.Run, cancellation.Token); }
        catch (OperationCanceledException) { Progress = Progress with { Phase = RunPhase.Stopped, Message = "Stopped. Inspect any interrupted operation before continuing." }; }
        catch (Exception error) { Error = error.Message; Progress = Progress with { Phase = RunPhase.NeedsReview, Message = error.Message }; }
        finally { Busy = false; Paused = false; Notify(); }
    }

    public void TogglePause()
    {
        if (!Busy || coordinator is null) return;
        if (Paused) coordinator.Resume(); else coordinator.Pause();
        Paused = !Paused;
        Message = Paused ? "Pause requested. The current transaction will finish verification first." : "Resumed.";
        Notify();
    }
    public void Stop() { cancellation?.Cancel(); coordinator?.Resume(); Notify(); }
    public void AcknowledgeReview() { EnsureIdle(); store.AcknowledgeUnresolvedOperations(); Notify(); }
    public void ClearError() { Error = null; Notify(); }
    public void ShowError(Exception error) { Error = error.Message; Notify(); }
    public IReadOnlyList<CharacterState> States => Demo ? simulatedGame?.States ?? [] : liveGame?.KnownStates() ?? [];
    public IReadOnlyList<CharacterState> ProductionStates => States.Where(s => Producers.Any(c => c.Enabled && c.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
    public string CharacterStatus(string name, bool connected)
    {
        lock (activityLock)
        {
            if (Busy && characterProgress.TryGetValue(name, out var progress))
                return progress.Phase switch
                {
                    RunPhase.WaitingForCollection => "Waiting for courier",
                    RunPhase.PoolingGold => "Pooling gold",
                    RunPhase.WaitingForDeposit => "Waiting for deposit",
                    _ => progress.Phase.ToString()
                };
        }
        return connected ? "Connected" : "Offline";
    }
    void EnsureIdle() { if (Busy || Recording) throw new InvalidOperationException("Finish the current operation first."); }
    void Notify() => Changed?.Invoke();

    static AppConfiguration DemoConfiguration() => new(
        [new("Aisling"), new("Rowan"), new("Celandine"), new("Muir")],
        new() { Courier = "Aisling", Recipient = "Vaultkeeper", RecipientMap = 167, RecipientX = 12, RecipientY = 10, BarsPerTrade = 3 }, new(),
        new("Abel banker", 167, 10, 10, "Gold Bar", 50_500_000,
            [new(DialogActionKind.Choice, "Simulated bank menu", "Make gold bar")], DateTimeOffset.UtcNow));
}
