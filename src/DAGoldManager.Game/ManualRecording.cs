using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Types;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using DAGoldManager.Core;
using DAQuester.Core;

namespace DAGoldManager.Game;

// Connects native, manually issued actions to semantic setup data. It never sends commands.
/// <summary>Observes allowed crafting/trade packets and detaches listeners when recording stops.</summary>
public sealed class ManualRecording : IDisposable
{
    readonly CharacterConnection character;
    readonly SetupRecorder recorder;
    readonly Action changed;
    TradeMonitor? sampleTrade;
    CharacterSnapshot? tradeStart;
    bool tradeRecorded, failed;
    bool selectedKnownBar;
    string? sampleOfferName;
    readonly List<ServerExchangeMessage> sampleMessages = [];
    readonly object gate = new();
    public string? Error { get; private set; }

    public ManualRecording(CharacterConnection character, SetupRecorder recorder, Action changed)
    {
        this.character = character;
        this.recorder = recorder;
        this.changed = changed;
        character.Connection.NativeGameAction += OnAction;
        character.Connection.Tracker.Observed += OnServer;
        character.Session.Changed += OnState;
    }

    void OnAction(ClientPacket packet)
    {
        lock (gate) RecordAction(packet);
    }

    void RecordAction(ClientPacket packet)
    {
        if (failed) return;
        try
        {
            var message = ClientMessageFactory.Default.Create(packet);
            var state = character.Snapshot;
            if (message is ClientExchangeMessage exchange && exchange.Action == ExchangeClientActionType.AddItem && exchange.Slot is { } slot)
                selectedKnownBar = state.Inventory.TryGetValue(slot, out var item) &&
                    item.Name.Equals(recorder.Recipe?.BarName ?? "Gold Bar", StringComparison.OrdinalIgnoreCase);
            if (message is ClientRequestObjectInfoMessage request && request.InteractionType == InteractionType.Entity &&
                request.TargetId is { } npcId && state.Entities.TryGetValue(npcId, out var npc) && !npc.IsPlayer)
            {
                recorder.BeginNpc(state, npc);
            }
            else if (state.Dialog is { } dialog)
            {
                if (message is ClientMerchantMessage merchant && merchant.EntityId == dialog.EntityId)
                {
                    if (merchant.Arguments.Count == 1)
                        recorder.RecordAction(new(DialogActionKind.Input, dialog.Text, merchant.Arguments[0]));
                    else
                    {
                        var choice = dialog.Choices.SingleOrDefault(c => c.Pursuit == merchant.PursuitId);
                        if (choice is not null) recorder.RecordAction(new(DialogActionKind.Choice, dialog.Text, choice.Text));
                    }
                }
                if (message is ClientPursuitMessage pursuit && pursuit.EntityId == dialog.EntityId)
                {
                    if (pursuit.ArgsType == DialogArgsType.MenuChoice)
                    {
                        var choice = dialog.Choices.SingleOrDefault(c => c.Option == pursuit.MenuChoice);
                        if (choice is null) throw new InvalidOperationException("Recorded menu choice could not be identified.");
                        recorder.RecordAction(new(DialogActionKind.Choice, dialog.Text, choice.Text));
                    }
                    else if (pursuit.ArgsType == DialogArgsType.TextInput && pursuit.TextInputs.Count == 1)
                        recorder.RecordAction(new(DialogActionKind.Input, dialog.Text, pursuit.TextInputs[0]));
                    else if (dialog.HasNext && pursuit.StepId == dialog.Step + 1)
                        recorder.RecordAction(new(DialogActionKind.Next, dialog.Text));
                }
            }
            changed();
        }
        catch (Exception error) { Fail(error); }
    }

    void OnServer(byte opcode, byte[] payload)
    {
        lock (gate) RecordServer(opcode, payload);
    }

    void RecordServer(byte opcode, byte[] payload)
    {
        if (failed) return;
        try
        {
            if (opcode == 0x0A && ServerMessageFactory.Default.Create(new ServerPacket(opcode, payload)) is ServerSystemMessage notice)
            {
                recorder.RecordCooldownNotice(notice.Message);
                changed();
            }
            if (opcode != 0x42) return;
            var message = ExchangeReader.Read(payload);
            if (message.Event == ExchangeServerEventType.Started)
            {
                sampleTrade = new(recorder.Recipe?.OfferName ?? recorder.Recipe?.BarName ?? "Gold Bar");
                sampleMessages.Clear();
                sampleOfferName = null;
                selectedKnownBar = false;
                tradeStart = character.Snapshot;
                tradeRecorded = false;
            }
            if (message.Event == ExchangeServerEventType.ItemAdded && message.Party == ExchangeParty.You && selectedKnownBar &&
                !sampleMessages.Any(m => m.Event == ExchangeServerEventType.ItemAdded))
            {
                // Display names can include durability. Learn one only from a manually selected, known bar slot.
                sampleOfferName = System.Text.RegularExpressions.Regex.Replace(message.ItemName ?? "", @"\(\d+\)$", "").Trim();
                sampleTrade = new(sampleOfferName);
                foreach (var previous in sampleMessages) sampleTrade.Observe(previous);
            }
            if (sampleMessages.Count >= 200) throw new InvalidOperationException("Sample trade exceeded the recording limit. Restart recording.");
            sampleMessages.Add(message);
            sampleTrade?.Observe(message);
            VerifySample();
            changed();
        }
        catch (Exception error) { Fail(error); }
    }

    void OnState(CharacterSnapshot state)
    {
        lock (gate) RecordState(state);
    }

    void RecordState(CharacterSnapshot state)
    {
        if (failed) return;
        try
        {
            recorder.ObserveState(state);
            VerifySample();
            changed();
        }
        catch (Exception error) { Fail(error); }
    }

    void VerifySample()
    {
        if (tradeRecorded || tradeStart is null || sampleTrade is null) return;
        var view = sampleTrade.View;
        var state = character.Snapshot;
        string barName = recorder.Recipe?.BarName ?? "Gold Bar";
        if (state.Generation != tradeStart.Generation || view.Cancelled || view.UnexpectedItem || view.OtherBars != 0 ||
            view.OwnGold != 0 || view.OtherGold != 0 || state.Gold != tradeStart.Gold) return;
        if (TransferVerification.IsComplete(view.LocalAccepted, view.PartnerAccepted, view.Completed,
            view.OwnBars, tradeStart.Count(barName), state.Count(barName)))
        {
            recorder.RecordVerifiedTrade(view.OwnBars, sampleOfferName ?? recorder.Recipe?.OfferName ?? barName);
            tradeRecorded = true;
        }
    }

    void Fail(Exception error)
    {
        failed = true;
        Error = error.Message;
        changed();
    }

    public void Dispose()
    {
        character.Connection.NativeGameAction -= OnAction;
        character.Connection.Tracker.Observed -= OnServer;
        character.Session.Changed -= OnState;
    }
}
