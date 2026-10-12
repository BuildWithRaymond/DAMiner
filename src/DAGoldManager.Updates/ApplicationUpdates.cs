namespace DAGoldManager.Updates;

/// <summary>Serializes manual/background release checks and stages verified downloads for next launch.</summary>
/// <remarks>Contains no restart action. Preview injects no client, so it performs no update I/O.</remarks>
public sealed class ApplicationUpdates : IDisposable
{
    readonly IUpdateClient? client;
    readonly UpdatePreferencesStore store;
    readonly SemaphoreSlim checkLock = new(1, 1);
    readonly CancellationTokenSource shutdown = new();
    Task? automaticLoop;

    public ApplicationUpdates(IUpdateClient? client, string settingsDirectory, string currentVersion, string defaultRepository = "")
    {
        this.client = client;
        store = new(settingsDirectory);
        CurrentVersion = currentVersion;
        Preferences = client is null ? new() : store.Load(new(GitHubRepository.Normalize(defaultRepository)));
        DescribeAvailability();
    }

    public event Action? Changed;
    public string CurrentVersion { get; }
    public UpdatePreferences Preferences { get; private set; }
    public UpdateStatus Status { get; private set; } = new(UpdatePhase.Idle, "");
    public bool Working => checkLock.CurrentCount == 0;
    public bool Preview => client is null;
    public bool CanCheck => !Preview && client!.IsInstalled && Preferences.Repository.Length > 0 && !Working;

    public void SavePreferences(UpdatePreferences preferences)
    {
        if (Preview) throw new InvalidOperationException("Updates are disabled in simulated preview.");
        if (Working) throw new InvalidOperationException("Wait for the update check or download to finish.");
        string repository = GitHubRepository.Normalize(preferences.Repository);
        if (client!.PendingVersion is not null && !repository.Equals(Preferences.Repository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Install the downloaded update before changing its release repository.");
        var saved = preferences with { Repository = repository };
        store.Save(saved);
        Preferences = saved;
        DescribeAvailability();
    }

    public async Task CheckAsync(bool automatic = false, CancellationToken cancellation = default)
    {
        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shutdown.Token);
        // A manual click racing the automatic check joins neither operation; simply keep the active
        // check. Two simultaneous downloads must never compete over the pending package.
        if (!await checkLock.WaitAsync(0, interrupted.Token)) return;
        try
        {
            if (!DescribeAvailability() || automatic && !Preferences.Automatic) return;
            Report(new(UpdatePhase.Checking, "Checking stable releases…"));
            string? offered = await client!.CheckAsync(Preferences.Repository, interrupted.Token);
            if (offered is null || Version.Parse(offered) <= Version.Parse(CurrentVersion))
            {
                Report(new(UpdatePhase.Current, "DAMiner is up to date."));
                return;
            }
            Report(new(UpdatePhase.Downloading, $"Downloading DAMiner {offered}…", offered));
            await client.DownloadAsync(offered, percent =>
                Report(new(UpdatePhase.Downloading, $"Downloading DAMiner {offered}…", offered, Math.Clamp(percent, 0, 100))), interrupted.Token);
            // The client exposes a pending version only after verified download completion.
            if (client.PendingVersion != offered) throw new InvalidDataException("Downloaded update was not confirmed.");
            Report(new(UpdatePhase.Ready, "Update downloaded. Installs next time you open DAMiner.", offered, 100));
        }
        catch (OperationCanceledException) when (interrupted.IsCancellationRequested)
        {
            Report(new(UpdatePhase.Idle, "Update check cancelled."));
        }
        catch (Exception error)
        {
            // Update failures never become game-operation errors or stop a run.
            Report(new(UpdatePhase.Failed, $"Update unavailable: {error.Message}"));
        }
        finally
        {
            checkLock.Release();
            Changed?.Invoke();
        }
    }

    public void Start()
    {
        if (!Preview && automaticLoop is null) automaticLoop = CheckPeriodicallyAsync();
    }

    async Task CheckPeriodicallyAsync()
    {
        try
        {
            await CheckAsync(automatic: true, shutdown.Token);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            while (await timer.WaitForNextTickAsync(shutdown.Token))
                await CheckAsync(automatic: true, shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }

    bool DescribeAvailability()
    {
        if (Preview) Report(new(UpdatePhase.Disabled, "Updates disabled in simulated preview."));
        else if (!client!.IsInstalled) Report(new(UpdatePhase.Unavailable, client.UnavailableReason ?? "Use the DAMiner portable release or installer to enable updates."));
        else if (client.PendingVersion is { } pending) Report(new(UpdatePhase.Ready, "Update downloaded. Installs next time you open DAMiner.", pending, 100));
        else if (Preferences.Repository.Length == 0) Report(new(UpdatePhase.NotConfigured, "Choose your GitHub release repository when DAMiner is published."));
        else if (!Preferences.Automatic) Report(new(UpdatePhase.Disabled, "Automatic downloads off. You can still check manually."));
        else { Report(new(UpdatePhase.Idle, "Automatic downloads on. Checks at startup and every six hours.")); return true; }
        return !Preview && client!.IsInstalled && client.PendingVersion is null && Preferences.Repository.Length > 0;
    }

    void Report(UpdateStatus status) { Status = status; Changed?.Invoke(); }
    public void Dispose() => shutdown.Cancel();
}
