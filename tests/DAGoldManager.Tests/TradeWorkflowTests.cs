using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAGoldManager.Core;
using DAGoldManager.Game;
using DAQuester.Core;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class TradeWorkflowTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArrivalWaitsForBothPlayersViewsBeforeOpeningOneTrade(bool senderViewIsLate)
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var late = senderViewIsLate ? world.Sender : world.Courier;
        var partner = senderViewIsLate ? world.Courier : world.Sender;
        late.SeeAt(partner, new(16, 10));

        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await Task.Delay(80, deadline.Token);
        Assert.False(trade.IsCompleted);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        Assert.Equal(0, world.Courier.Remote.Peer.Available);

        late.See(partner);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        await world.CompleteGoldExchangeAsync(deadline.Token);
        await trade;
        Assert.Equal(1, world.Sender.Actions.Count(a => a == ExchangeClientActionType.BeginExchange));
        Assert.Equal(1, world.Sender.Actions.Count(a => a == ExchangeClientActionType.SetGold));
        Assert.Equal(1, world.Sender.Actions.Count(a => a == ExchangeClientActionType.Accept));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArrivalWaitsForAMissingManagedPlayerDraw(bool senderViewIsLate)
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var late = senderViewIsLate ? world.Sender : world.Courier;
        var partner = senderViewIsLate ? world.Courier : world.Sender;
        late.Publish(late.Character.Snapshot with { Entities = ImmutableDictionary<uint, Entity>.Empty });
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await Task.Delay(80, deadline.Token);
        Assert.False(trade.IsCompleted);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        late.See(partner);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        await world.CompleteGoldExchangeAsync(deadline.Token);
        await trade;
        Assert.Equal(1, world.Sender.Actions.Count(a => a == ExchangeClientActionType.BeginExchange));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConflictingVisiblePlayerIdentityCannotStartATrade(bool duplicateName)
    {
        using var world = new TradeWorld();
        var state = world.Sender.Character.Snapshot;
        var conflicting = new Entity(99, "Courier", new(11, 10), true) { IsPlayer = true };
        world.Sender.Publish(state with
        {
            Entities = (duplicateName ? state.Entities : ImmutableDictionary<uint, Entity>.Empty).Add(99, conflicting)
        });
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => trade.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        Assert.Equal(0, world.Courier.Remote.Peer.Available);
    }

    [Fact]
    public async Task ParticipantsWhoseOwnPositionsAreApartTimeOutWithoutSendingATrade()
    {
        using var world = new TradeWorld(tradeTimeoutSeconds: 1);
        world.Courier.Publish(world.Courier.Character.Snapshot with { Position = new(14, 10) });
        world.Sender.See(world.Courier);
        world.Courier.See(world.Sender);
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, default);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => trade.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Contains("settle into trade range", error.Message);
        Assert.Contains("Sender own: map 502 (10,10)", error.Message);
        Assert.Contains("Courier own: map 502 (14,10)", error.Message);
        Assert.Contains("Sender sees Courier: (14,10)", error.Message);
        Assert.Contains("Courier sees Sender: (10,10)", error.Message);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        Assert.Equal(0, world.Courier.Remote.Peer.Available);
    }

    [Fact]
    public async Task CancellationWhileArrivalViewsSettleSendsNoTradeCommand()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        world.Sender.SeeAt(world.Courier, new(16, 10));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await Task.Delay(80, deadline.Token);
        Assert.False(trade.IsCompleted);
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trade);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        Assert.Equal(0, world.Courier.Remote.Peer.Available);
    }

    [Fact]
    public async Task ChangedIdentityDuringArrivalSettlingStopsWithoutSendingATrade()
    {
        using var world = new TradeWorld();
        world.Sender.SeeAt(world.Courier, new(16, 10));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, default);
        await Task.Delay(80);
        Assert.False(trade.IsCompleted);
        world.Courier.Publish(world.Courier.Character.Snapshot with { Id = 99 });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => trade.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("identity", error.Message);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        Assert.Equal(0, world.Courier.Remote.Peer.Available);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedBalancesDuringArrivalSettlingCannotStartATrade(bool goldChanged)
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        world.Sender.SeeAt(world.Courier, new(16, 10));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await Task.Delay(80, deadline.Token);
        Assert.False(trade.IsCompleted);
        if (goldChanged) world.Sender.Publish(world.Sender.Character.Snapshot with { Gold = 1_000_001 });
        else world.Courier.SetBars(1);
        world.Sender.See(world.Courier);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => trade);
        Assert.Contains("balances changed", error.Message);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        Assert.Equal(0, world.Courier.Remote.Peer.Available);
    }

    [Fact]
    public async Task InitialOpeningRechecksArrivalAfterAcquiringTheWireLock()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var wireGate = (SemaphoreSlim)typeof(ProxyConnection).GetField("toServer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world.Sender.Connection)!;
        await wireGate.WaitAsync(deadline.Token);
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        try
        {
            world.Sender.SeeAt(world.Courier, new(16, 10));
        }
        finally { wireGate.Release(); }
        await Task.Delay(80, deadline.Token);
        Assert.False(trade.IsCompleted);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        world.Sender.See(world.Courier);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        await world.CompleteGoldExchangeAsync(deadline.Token);
        await trade;
        Assert.Equal(1, world.Sender.Actions.Count(a => a == ExchangeClientActionType.BeginExchange));
    }

    [Fact]
    public async Task MovementOutOfRangeAfterOpeningStopsBeforeAnyOffer()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        world.Courier.Publish(world.Courier.Character.Snapshot with { Position = new(14, 10) });
        world.Sender.See(world.Courier);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => trade.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("out of range", error.Message);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        Assert.Equal(0, world.Courier.Remote.Peer.Available);
        Assert.Single(world.Sender.Actions);
    }

    [Fact]
    public async Task IgnoredEmptyOpeningCanRecoverWithoutRepeatingTheGoldOffer()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        // The first empty request gets no response. Only another empty opening is safe.
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        await world.CompleteGoldExchangeAsync(deadline.Token);
        await trade;

        Assert.Equal(2, world.Sender.Actions.Count(a => a == ExchangeClientActionType.BeginExchange));
        Assert.Equal(1, world.Sender.Actions.Count(a => a == ExchangeClientActionType.SetGold));
        Assert.Equal(1, world.Sender.Actions.Count(a => a == ExchangeClientActionType.Accept));
        Assert.Equal(1, world.Courier.Actions.Count(a => a == ExchangeClientActionType.Accept));
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
    }

    [Theory]
    [InlineData("one-sided-start")]
    [InlineData("zero-gold")]
    [InlineData("accepted")]
    [InlineData("cancelled")]
    [InlineData("quantity")]
    [InlineData("completion-before-start")]
    [InlineData("invalid-exchange")]
    [InlineData("refusal")]
    [InlineData("too-far-refusal")]
    [InlineData("gold-change")]
    [InlineData("inventory-change")]
    [InlineData("inventory-name-change")]
    [InlineData("identity-change")]
    public async Task AnyEvidenceOrBaselineChangePreventsAnotherEmptyOpening(string change)
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(2300));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        switch (change)
        {
            case "one-sided-start": world.StartSender(); break;
            case "zero-gold": world.Courier.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.GoldAdded, Party = ExchangeParty.Them, GoldAmount = 0 }); break;
            case "accepted": world.Courier.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.Accepted, Party = ExchangeParty.You, Message = "" }); break;
            case "cancelled": world.Courier.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.Cancelled, Party = ExchangeParty.You, Message = "" }); break;
            case "quantity": world.Courier.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.QuantityPrompt, Slot = 1 }); break;
            case "completion-before-start": world.Courier.Observe(new ServerSystemMessage { MessageType = WorldMessageType.Popup, Message = "You have exchanged." }); break;
            case "invalid-exchange": world.Courier.Connection.Tracker.Observe(0x42, [255]); break;
            case "refusal": world.Courier.Observe(new ServerSystemMessage { MessageType = WorldMessageType.Popup, Message = "You cannot exchange now." }); break;
            case "too-far-refusal": world.Courier.Observe(new ServerSystemMessage { MessageType = WorldMessageType.Popup, Message = "You are too far away." }); break;
            case "gold-change": world.Courier.Publish(world.Courier.Character.Snapshot with { Gold = 999_999 }); break;
            case "inventory-change": world.Sender.SetBars(3); break;
            case "inventory-name-change": world.Sender.Observe(new ServerAddInventoryMessage { Slot = 1, Name = "Fictional item", Quantity = 2, IsStackable = true }); break;
            case "identity-change": world.Sender.Publish(world.Sender.Character.Snapshot with { Id = 99 }); break;
        }
        await Assert.ThrowsAnyAsync<Exception>(() => trade);
        await world.Sender.ExpectOnlyCancellationAsync();
        await world.Courier.ExpectOnlyCancellationAsync();
    }

    [Fact]
    public async Task OpeningGuardRunsAfterTheWireLockAndPreservesALateWindow()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        var wireGate = (SemaphoreSlim)typeof(ProxyConnection).GetField("toServer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world.Sender.Connection)!;
        await wireGate.WaitAsync(deadline.Token);
        try
        {
            await Task.Delay(2200, deadline.Token);
            world.StartSender();
            world.StartCourier();
        }
        finally { wireGate.Release(); }
        await world.CompleteGoldExchangeAsync(deadline.Token, alreadyStarted: true);
        await trade;
        Assert.Equal(1, world.Sender.Actions.Count(a => a == ExchangeClientActionType.BeginExchange));
    }

    [Fact]
    public async Task DelayedOpeningWriteCannotMakeTheFollowingRequestImmediatelyOverdue()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        var wireGate = (SemaphoreSlim)typeof(ProxyConnection).GetField("toServer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world.Sender.Connection)!;
        await wireGate.WaitAsync(deadline.Token);
        try { await Task.Delay(3200, deadline.Token); }
        finally { wireGate.Release(); }
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        await Task.Delay(300, deadline.Token);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        await world.CompleteGoldExchangeAsync(deadline.Token);
        await trade;
    }

    [Fact]
    public async Task ExternalOpeningIsNeverRepeated()
    {
        using var world = new TradeWorld();
        world.Gateway.Configure(new() { TradeTimeoutSeconds = 5 }, world.Recipe, "Courier");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(2300));
        var trade = world.Gateway.TransferBarsAsync("Sender", "Courier", 1, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trade);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
    }

    [Fact]
    public async Task CancellationDuringEmptyOpeningWaitSendsNothingElse()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trade);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
    }

    [Fact]
    public async Task EmptyOpeningStopsAfterThreeRequestsWithinItsOriginalTimeout()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        for (int request = 0; request < 3; request++)
            await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => trade);
        Assert.Contains("5 seconds", error.Message);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
    }

    [Fact]
    public void GenericAllowedNoticeRemainsDiagnosticContextWithoutExchangeEvidence()
    {
        using var world = new TradeWorld();
        using var observed = new ObservedTrade(world.Sender.Character, "Gold Bar");
        world.Sender.Observe(new ServerSystemMessage { MessageType = WorldMessageType.Popup, Message = "Please wait a moment." });
        Assert.Equal("Please wait a moment.", observed.LastNotice);
        Assert.Null(observed.Error);
        Assert.False(observed.HasExchangeEvidence);
        Assert.False(observed.Monitor.View.Completed);
    }

    [Theory]
    [InlineData("You cannot exchange now.")]
    [InlineData("You are too far away.")]
    public async Task ReceiverRefusalStopsTheSenderOpeningWaitImmediately(string refusal)
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        world.Courier.Observe(new ServerSystemMessage { MessageType = WorldMessageType.Popup, Message = refusal });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => trade.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("Server refused", error.Message);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
    }

    [Fact]
    public async Task MissingOpeningIncludesTheReceiversGenericServerNotice()
    {
        using var world = new TradeWorld(tradeTimeoutSeconds: 1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var trade = world.Gateway.TransferGoldAsync("Sender", "Courier", 250_000, deadline.Token);
        await world.Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, deadline.Token);
        world.Courier.Observe(new ServerSystemMessage { MessageType = WorldMessageType.Popup, Message = "Please wait a moment." });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => trade);
        Assert.Contains("Receiver's last server notice: Please wait a moment.", error.Message);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
    }

    [Fact]
    public async Task LateCompletionPopupsAreClosedBeforeTheNextLocalCollectionWithoutRepeatingATrade()
    {
        using var world = new TradeWorld();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cancellation = deadline.Token;

        var first = world.Gateway.TransferBarsAsync("Sender", "Courier", 1, cancellation);
        await world.CompleteServerExchangeAsync(senderBarsBefore: 2, courierBarsBefore: 0, cancellation);
        await first;
        // Both exact inventory changes verify a local trade before its optional popup arrives.
        await world.Gateway.FinishTradeDialogsAsync("Sender", "Courier", cancellation);
        Assert.Equal(0, world.Sender.Client.Peer.Available);
        Assert.Equal(0, world.Courier.Client.Peer.Available);

        world.PublishCompletions();
        var second = world.Gateway.TransferBarsAsync("Sender", "Courier", 1, cancellation);
        // Preparation must recover the late popups without a manual client action.
        await world.Sender.ExpectPopupClosedAsync(cancellation);
        await world.Courier.ExpectPopupClosedAsync(cancellation);
        await world.CompleteServerExchangeAsync(senderBarsBefore: 1, courierBarsBefore: 1, cancellation);
        world.PublishCompletions();
        await second;
        await world.Gateway.FinishTradeDialogsAsync("Sender", "Courier", cancellation);
        await world.Sender.ExpectPopupClosedAsync(cancellation);
        await world.Courier.ExpectPopupClosedAsync(cancellation);

        Assert.Equal(0, world.Sender.Character.Snapshot.Count("Gold Bar"));
        Assert.Equal(2, world.Courier.Character.Snapshot.Count("Gold Bar"));
        Assert.False(world.Sender.Character.TradeWindow.BlocksNewTrade);
        Assert.False(world.Courier.Character.TradeWindow.BlocksNewTrade);
        Assert.Equal(2, world.Sender.Actions.Count(a => a == ExchangeClientActionType.BeginExchange));
        Assert.Equal(2, world.Sender.Actions.Count(a => a == ExchangeClientActionType.Accept));
        Assert.Equal(2, world.Courier.Actions.Count(a => a == ExchangeClientActionType.Accept));
        Assert.DoesNotContain(ExchangeClientActionType.Cancel, world.Sender.Actions);
        Assert.DoesNotContain(ExchangeClientActionType.Cancel, world.Courier.Actions);
        Assert.Equal(0, world.Sender.Remote.Peer.Available);
        Assert.Equal(0, world.Courier.Remote.Peer.Available);
    }

    sealed class TradeWorld : IDisposable
    {
        public Participant Sender { get; } = new("Sender", 1, 10, 2);
        public Participant Courier { get; } = new("Courier", 2, 11, 0);
        public GameGateway Gateway { get; }
        public CraftRecipe Recipe { get; }

        public TradeWorld(int tradeTimeoutSeconds = 5)
        {
            Recipe = new CraftRecipe("Fictional banker", 502, 10, 10, "Gold Bar", 1_000_000,
                [], DateTimeOffset.UtcNow);
            Gateway = new(new() { TradeTimeoutSeconds = tradeTimeoutSeconds }, Recipe,
                _ => throw new InvalidOperationException("No real credentials."), "");
            var field = typeof(GameGateway).GetField("characters", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var characters = (ConcurrentDictionary<string, CharacterConnection>)field.GetValue(Gateway)!;
            characters["Sender"] = Sender.Character;
            characters["Courier"] = Courier.Character;
            Sender.See(Courier);
            Courier.See(Sender);
        }

        public void StartSender() => Sender.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Courier" });
        public void StartCourier() => Courier.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.Started, TargetId = 1, TargetName = "Sender" });

        public async Task CompleteGoldExchangeAsync(CancellationToken cancellation, bool alreadyStarted = false)
        {
            if (!alreadyStarted) { StartSender(); StartCourier(); }
            var offer = await Sender.ExpectExchangeAsync(ExchangeClientActionType.SetGold, 2, cancellation);
            Assert.Equal((uint)250_000, offer.GoldAmount);
            Sender.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.GoldAdded, Party = ExchangeParty.You, GoldAmount = 250_000 });
            Courier.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.GoldAdded, Party = ExchangeParty.Them, GoldAmount = 250_000 });
            await Sender.ExpectExchangeAsync(ExchangeClientActionType.Accept, 2, cancellation);
            await Courier.ExpectExchangeAsync(ExchangeClientActionType.Accept, 1, cancellation);
            Sender.Publish(Sender.Character.Snapshot with { Gold = 750_000 });
            Courier.Publish(Courier.Character.Snapshot with { Gold = 1_250_000 });
        }

        public async Task CompleteServerExchangeAsync(int senderBarsBefore, int courierBarsBefore,
            CancellationToken cancellation)
        {
            await Sender.ExpectExchangeAsync(ExchangeClientActionType.BeginExchange, 2, cancellation);
            Sender.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Courier" });
            Courier.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.Started, TargetId = 1, TargetName = "Sender" });

            await Sender.ExpectExchangeAsync(ExchangeClientActionType.AddItem, 2, cancellation);
            if (senderBarsBefore > 1)
            {
                Sender.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.QuantityPrompt, Slot = 1 });
                var quantity = await Sender.ExpectExchangeAsync(ExchangeClientActionType.AddStackableItem, 2, cancellation);
                Assert.Equal((byte)1, quantity.Quantity);
            }
            Sender.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.ItemAdded, Party = ExchangeParty.You,
                ItemIndex = 1, ItemSprite = 1, ItemName = "Gold Bar" });
            Courier.Observe(new ServerExchangeMessage { Event = ExchangeServerEventType.ItemAdded, Party = ExchangeParty.Them,
                ItemIndex = 1, ItemSprite = 1, ItemName = "Gold Bar" });
            await Sender.ExpectExchangeAsync(ExchangeClientActionType.Accept, 2, cancellation);
            await Courier.ExpectExchangeAsync(ExchangeClientActionType.Accept, 1, cancellation);

            Sender.SetBars(senderBarsBefore - 1);
            Courier.SetBars(courierBarsBefore + 1);
        }

        public void PublishCompletions()
        {
            Sender.Observe(new ServerSystemMessage { MessageType = WorldMessageType.Popup, Message = "You have exchanged." });
            Courier.Observe(new ServerSystemMessage { MessageType = WorldMessageType.Popup, Message = "You have exchanged." });
        }

        public void Dispose() { Sender.Dispose(); Courier.Dispose(); }
    }

    sealed class Participant : IDisposable
    {
        public SocketPair Client { get; } = new();
        public SocketPair Remote { get; } = new();
        public ProxyConnection Connection { get; }
        public CharacterConnection Character { get; }
        public List<ExchangeClientActionType> Actions { get; } = [];
        readonly ClientPacketEncryptor clientCrypto = new();
        readonly ServerPacketEncryptor serverCrypto = new();

        public Participant(string name, uint id, int x, int bars)
        {
            Connection = new(Client.Connection, Remote.Connection, id, _ => Task.FromResult(0));
            Connection.Tracker.Identity(name, id);
            Character = new(Connection, new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json")), 400);
            Publish(Character.Snapshot with { Map = 502, Position = new(x, 10), Gold = 1_000_000,
                InventoryKnown = true, Weight = bars, MaxWeight = 100 });
            if (bars > 0) SetBars(bars);
        }

        public void See(Participant other)
        {
            var state = other.Character.Snapshot;
            SeeAt(other, state.Position!);
        }

        public void SeeAt(Participant other, Tile position)
        {
            var state = other.Character.Snapshot;
            Publish(Character.Snapshot with { Entities = ImmutableDictionary<uint, Entity>.Empty.Add(state.Id!.Value,
                new(state.Id.Value, state.Name!, position, true) { IsPlayer = true }) });
        }

        public void Publish(CharacterSnapshot snapshot) =>
            typeof(StateTracker).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Connection.Tracker, [snapshot]);

        public void SetBars(int bars)
        {
            if (bars == 0) Observe(new ServerRemoveInventoryMessage { Slot = 1 });
            else Observe(new ServerAddInventoryMessage { Slot = 1, Name = "Gold Bar", Quantity = (uint)bars, IsStackable = true });
        }

        public void Observe(ServerMessage message)
        {
            var command = ServerMessageFactory.Default.GetMessageCommand(message.GetType())!.Value;
            var builder = new NetworkPacketBuilder(command);
            try
            {
                message.Serialize(ref builder);
                var packet = (ServerPacket)builder.ToPacket();
                Connection.Tracker.Observe((byte)command, packet.Data);
            }
            finally { builder.Dispose(); }
        }

        public async Task<ClientExchangeMessage> ExpectExchangeAsync(ExchangeClientActionType action, uint target,
            CancellationToken cancellation)
        {
            var (command, data) = await ReadFrameAsync(Remote.Peer, cancellation);
            var plain = (ClientPacket)clientCrypto.Decrypt(new ClientPacket(command, data));
            var message = Assert.IsType<ClientExchangeMessage>(ClientMessageFactory.Default.Create(plain));
            Actions.Add(message.Action);
            Assert.Equal(action, message.Action);
            Assert.Equal(target, message.TargetId);
            return message;
        }

        public async Task ExpectOnlyCancellationAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            while (Remote.Peer.Available > 0)
            {
                var (command, data) = await ReadFrameAsync(Remote.Peer, deadline.Token);
                var plain = (ClientPacket)clientCrypto.Decrypt(new ClientPacket(command, data));
                var message = Assert.IsType<ClientExchangeMessage>(ClientMessageFactory.Default.Create(plain));
                Assert.Equal(ExchangeClientActionType.Cancel, message.Action);
            }
        }

        public async Task ExpectPopupClosedAsync(CancellationToken cancellation)
        {
            var (command, data) = await ReadFrameAsync(Client.Peer, cancellation);
            var plain = (ServerPacket)serverCrypto.Decrypt(new ServerPacket(command, data));
            var message = Assert.IsType<ServerSystemMessage>(ServerMessageFactory.Default.Create(plain));
            Assert.Equal(WorldMessageType.ClosePopup, message.MessageType);
        }

        public void Dispose() { Client.Dispose(); Remote.Dispose(); }
    }

    static async Task<(byte Command, byte[] Data)> ReadFrameAsync(TcpClient peer, CancellationToken cancellation)
    {
        var header = new byte[4];
        await peer.GetStream().ReadExactlyAsync(header, cancellation);
        Assert.Equal((byte)0xAA, header[0]);
        int length = (header[1] << 8 | header[2]) - 1;
        Assert.InRange(length, 0, ushort.MaxValue);
        var data = new byte[length];
        await peer.GetStream().ReadExactlyAsync(data, cancellation);
        return (header[3], data);
    }

    sealed class SocketPair : IDisposable
    {
        public TcpClient Connection { get; } = new();
        public TcpClient Peer { get; }
        public SocketPair()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Connection.Connect((IPEndPoint)listener.LocalEndpoint);
                Peer = listener.AcceptTcpClient();
            }
            finally { listener.Stop(); }
        }
        public void Dispose() { Connection.Dispose(); Peer.Dispose(); }
    }
}
