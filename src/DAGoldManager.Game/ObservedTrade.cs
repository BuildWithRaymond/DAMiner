using System.Diagnostics;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Observes one attempt's fresh server responses; it never sends or retries a request.</summary>
public sealed class ObservedTrade : IDisposable
{
    readonly CharacterConnection character;
    readonly long generation;
    readonly long revision;
    volatile string? error;
    volatile string? lastNotice;
    volatile bool hasExchangeEvidence;
    public TradeMonitor Monitor { get; }
    public string? Error => error;
    public string? LastNotice => lastNotice;
    // Never clear this latch: a later Started packet can replace the current view.
    public bool HasExchangeEvidence => hasExchangeEvidence;

    public ObservedTrade(CharacterConnection character, string barName)
    {
        this.character = character;
        generation = character.Snapshot.Generation;
        revision = character.Snapshot.Revision;
        Monitor = new(barName);
        character.Connection.Tracker.Observed += Observe;
    }

    void Observe(byte opcode, byte[] payload)
    {
        if (opcode is not (0x42 or 0x0A)) return;
        try
        {
            if (character.Snapshot.Generation != generation) { error = "Trade connection changed."; return; }
            if (opcode == 0x42)
            {
                hasExchangeEvidence = true;
                Monitor.Observe(ExchangeReader.Read(payload));
            }
            else if (character.Snapshot.Revision > revision &&
                ServerMessageFactory.Default.Create(new ServerPacket(opcode, payload)) is ServerSystemMessage message)
            {
                if ((byte)message.MessageType is not (1 or 2 or 3 or 4 or 6 or 8 or 9)) return;
                string text = message.Message.ReplaceLineEndings(" ").Trim();
                lastNotice = text.Length <= 400 ? text : text[..400];
                if (TradePopupCleanup.IsServerCompletion((byte)message.MessageType, message.Message))
                {
                    hasExchangeEvidence = true;
                    Monitor.ObserveCompletion(message.Message);
                    return;
                }
                if (!IsTradeNotice(text) && !IsRefusal(text)) return;
                hasExchangeEvidence = true;
                if (IsRefusal(text)) error = "Server refused the trade: " + lastNotice;
            }
        }
        catch (Exception failure) { error = "Trade response could not be decoded: " + failure.Message; }
    }

    static bool IsTradeNotice(string text) => !TradePopupCleanup.IsCompletion(text) &&
        (text.Contains("exchange", StringComparison.OrdinalIgnoreCase) || text.Contains("trade", StringComparison.OrdinalIgnoreCase));

    static bool IsRefusal(string text) => new[] { "can't exchange", "cannot exchange", "refusing to exchange",
        "refuses to exchange", "not accepting exchanges", "cannot trade", "can't trade", "unable to trade",
        "refusing to trade", "too far", "already trading", "already exchanging" }
        .Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    public async Task WaitAsync(Func<TradeView, bool> ready, TimeSpan timeout, CancellationToken cancellation,
        Func<string>? context = null, Action? validate = null,
        Func<TimeSpan, CancellationToken, Task>? whileWaiting = null)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        var elapsed = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                limit.Token.ThrowIfCancellationRequested();
                validate?.Invoke();
                if (!character.Snapshot.Connected || character.Snapshot.Generation != generation)
                    throw new IOException("Trade connection disconnected or changed.");
                if (Error is not null) throw new InvalidOperationException(Error);
                var view = Monitor.View;
                if (view.Cancelled || view.UnexpectedItem) throw new InvalidOperationException("Trade was cancelled or contains unexpected items.");
                if (ready(view)) return;
                if (whileWaiting is not null) await whileWaiting(elapsed.Elapsed, limit.Token);
                await Task.Delay(25, limit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            string details = context?.Invoke() ?? $"Exchange window started: {Monitor.View.Started}.";
            string notice = LastNotice is null ? " No allowed server notice was received." : " Last server notice: " + LastNotice;
            throw new TimeoutException($"Trade response was not confirmed within {timeout.TotalSeconds:0.###} seconds (waited {elapsed.Elapsed.TotalSeconds:0.###}). {details}{notice} Inspect the game before another run.");
        }
    }

    public void Dispose() => character.Connection.Tracker.Observed -= Observe;
}
