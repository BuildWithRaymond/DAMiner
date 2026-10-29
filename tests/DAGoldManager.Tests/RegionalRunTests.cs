using System.Collections.Concurrent;
using DAGoldManager.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class RegionalRunTests
{
    const int NoamBankMap = 10061;
    const long Reserve = 100_000;

    [Fact]
    public async Task NoamProducersStayAtTheirBankWhileCourierCollects()
    {
        var game = new RegionalGame(
            State("Runner", 50_500_000 + Reserve, AbelBank.Map),
            State("NoamProducer", 50_500_000, NoamBankMap));

        var result = await Run(game);

        Assert.Equal(2, result.CreatedBars);
        Assert.Equal(2, result.DeliveredBars);
        Assert.Contains(game.Approaches, p => p.Sender == "Runner" && p.Receiver == "NoamProducer");
        Assert.DoesNotContain(game.Approaches, p => p.Sender == "NoamProducer" && p.Receiver == "Runner");
        Assert.Equal(NoamBankMap, game.States["NoamProducer"].Map);
    }

    [Fact]
    public async Task LeftoversPoolInsideEachBankWithoutCrossingRegions()
    {
        var game = new RegionalGame(
            State("Runner", Reserve, AbelBank.Map),
            State("AbelProducer", 30_000_000, AbelBank.Map),
            State("NoamProducer", 30_000_000, NoamBankMap));

        var result = await Run(game);

        Assert.Equal(0, result.CreatedBars);
        Assert.Empty(game.GoldTransfers);
        Assert.Equal(60_000_000 + Reserve, result.EndingGold);
    }

    [Fact]
    public async Task NoamLeftoversStillFundAnotherNoamCrafter()
    {
        var game = new RegionalGame(
            State("Runner", Reserve, AbelBank.Map),
            State("NoamProducer1", 30_000_000, NoamBankMap),
            State("NoamProducer2", 30_000_000, NoamBankMap));

        var result = await Run(game);

        Assert.Equal(1, result.CreatedBars);
        Assert.Equal(1, result.DeliveredBars);
        Assert.Single(game.GoldTransfers);
        Assert.All(game.GoldTransfers, p => Assert.Equal(NoamBankMap, game.HomeBanks[p.Sender]));
        Assert.All(game.GoldTransfers, p => Assert.Equal(NoamBankMap, game.HomeBanks[p.Receiver]));
    }

    [Fact]
    public async Task PoolingReplansAfterLocalCaravanUsesTheLastFundingGold()
    {
        var game = new RegionalGame(
            State("Runner", Reserve, AbelBank.Map),
            State("NoamReceiver", 30_000_000, NoamBankMap),
            State("NoamDonor", 20_500_000, NoamBankMap) with { Map = 10000 })
            { LocalApproachFare = 10_000 };
        var ledger = new TestLedger();

        var result = await Run(game, ledger);

        Assert.Equal(0, result.CreatedBars);
        Assert.Equal(0, result.DeliveredBars);
        Assert.Equal(10_000, result.TravelGoldSpent);
        Assert.Empty(game.GoldTransfers);
        Assert.Equal(20_490_000, game.States["NoamDonor"].Gold);
        Assert.DoesNotContain(ledger.Operations, operation => operation.Outcome == OperationOutcome.NeedsReview);
    }

    [Fact]
    public async Task CourierKeepsFareReserveOutOfCraftingAndPooling()
    {
        var game = new RegionalGame(
            State("Runner", 50_550_000, AbelBank.Map),
            State("NoamProducer", 0, NoamBankMap));

        var result = await Run(game);

        Assert.Equal(0, result.CreatedBars);
        Assert.Equal(50_550_000, game.States["Runner"].Gold);
        Assert.Empty(game.GoldTransfers);
    }

    [Fact]
    public async Task BankArrivalFareCannotConsumeCourierCraftReserve()
    {
        var game = new RegionalGame(
            State("Runner", 50_500_000 + Reserve, AbelBank.Map),
            State("NoamProducer", 0, NoamBankMap)) { BankArrivalFare = 10_000 };

        var result = await Run(game);

        Assert.Equal(0, result.CreatedBars);
        Assert.Equal(10_000, result.TravelGoldSpent);
        Assert.True(game.States["Runner"].Gold >= Reserve);
    }

    [Fact]
    public async Task SameBankDonorFundsCourierReserveBeforeCourierSpends()
    {
        var game = new RegionalGame(
            State("Runner", 0, AbelBank.Map),
            State("AbelDonor", Reserve, AbelBank.Map),
            State("NoamProducer", 50_500_000, NoamBankMap));

        var result = await Run(game);

        Assert.Equal(1, result.DeliveredBars);
        var funding = Assert.Single(game.GoldTransfers);
        Assert.Equal(("AbelDonor", "Runner", Reserve), funding);
        Assert.Equal(Reserve, game.States["Runner"].Gold);
    }

    [Fact]
    public async Task ExactBarCostPeerFundsFareBeforeItsFirstCraft()
    {
        var game = new RegionalGame(
            State("Runner", 0, NoamBankMap),
            State("NoamProducer", 50_500_000, NoamBankMap),
            State("AbelProducer", 0, AbelBank.Map));

        var result = await Run(game);

        Assert.Equal(0, result.CreatedBars);
        Assert.Equal(("NoamProducer", "Runner", Reserve), Assert.Single(game.GoldTransfers));
        Assert.Empty(game.Crafts);
        Assert.Equal(Reserve, game.States["Runner"].Gold);
    }

    [Fact]
    public async Task MissingSameBankFareFundsStopsBeforeCourierCraftsOrTravels()
    {
        var game = new RegionalGame(
            State("Runner", Reserve - 1, AbelBank.Map),
            State("NoamProducer", 0, NoamBankMap));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(game));

        Assert.Contains("travel", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(game.Crafts);
        Assert.Empty(game.Approaches);
        Assert.Empty(game.Deliveries);
    }

    [Fact]
    public async Task NoamCourierReservesFareForExternalTemuairRecipient()
    {
        var game = new RegionalGame(State("Runner", 50_550_000, NoamBankMap));

        var result = await Run(game);

        Assert.Equal(0, result.CreatedBars);
        Assert.Equal(50_550_000, result.EndingGold);
    }

    [Fact]
    public async Task AllAbelRunCanSpendLastBarCostWithoutTravelReserve()
    {
        var game = new RegionalGame(State("Runner", 50_500_000, AbelBank.Map));

        var result = await Run(game);

        Assert.Equal(1, result.CreatedBars);
        Assert.Equal(0, result.EndingGold);
    }

    [Fact]
    public async Task ExpensiveBarsUsePeerSoCourierFundingCannotExceedCarryingLimit()
    {
        var game = new RegionalGame(
            State("Runner", GoldPlanner.CarryingLimit, AbelBank.Map),
            State("AbelProducer", Reserve, AbelBank.Map),
            State("NoamProducer", 0, NoamBankMap)) { BarCost = 99_950_000 };

        var result = await Run(game);

        Assert.Equal(1, result.CreatedBars);
        Assert.Equal("AbelProducer", Assert.Single(game.Crafts));
        Assert.True(game.States["Runner"].Gold >= Reserve);
    }

    [Fact]
    public async Task VerifiedTravelCostReconcilesAlongsideCraftCost()
    {
        var game = new RegionalGame(
            State("Runner", 50_500_000 + Reserve, AbelBank.Map),
            State("NoamProducer", 50_500_000, NoamBankMap)) { ChargeTravel = true };
        game.TravelSpending["Runner"] = 7_000;

        var result = await Run(game);

        Assert.Equal(2, result.DeliveredBars);
        Assert.Equal(20_000, result.TravelGoldSpent);
        Assert.Equal(80_000, result.EndingGold);
    }

    [Fact]
    public async Task UnaccountedGoldLossStillFailsFinalReconciliation()
    {
        var game = new RegionalGame(State("Runner", 50_500_000 + Reserve, AbelBank.Map))
            { UnexplainedDeliveryLoss = 1 };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(game));

        Assert.Contains("reconcile", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NegativeTravelCounterCannotCancelAnUnexplainedGoldGain()
    {
        var game = new RegionalGame(State("Runner", 50_500_000, AbelBank.Map))
            { UnexplainedDeliveryLoss = -1, CorruptTravelCounter = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(game));
    }

    [Fact]
    public async Task RecipientTravelSpendingIsOutsideProductionReconciliation()
    {
        var game = new RegionalGame(State("Runner", 50_500_000, AbelBank.Map));
        game.TravelSpending["Vaultkeeper"] = 50_000;

        var result = await Run(game);

        Assert.Equal(1, result.DeliveredBars);
        Assert.Equal(0, result.TravelGoldSpent);
    }

    static Task<RunResult> Run(RegionalGame game, TestLedger? ledger = null) => new RunCoordinator(game, ledger ?? new TestLedger(), new TestClock())
        .RunAsync(game.Roster, TestGame.DeliverySettings with { Courier = "Runner", BarsPerTrade = 16,
            BarCost = game.BarCost }, default);

    static CharacterState State(string name, long gold, int homeBank) =>
        new(name, gold, 0, 59, homeBank, 8, 8) { CraftBankMap = homeBank };

    sealed class RegionalGame : IGameGateway
    {
        public ConcurrentDictionary<string, CharacterState> States { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> HomeBanks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, long> TravelSpending { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentQueue<(string Sender, string Receiver)> Approaches { get; } = new();
        public ConcurrentQueue<(string Sender, string Receiver, long Amount)> GoldTransfers { get; } = new();
        public ConcurrentQueue<string> Crafts { get; } = new();
        public ConcurrentQueue<int> Deliveries { get; } = new();
        public bool ChargeTravel { get; init; }
        public long BankArrivalFare { get; init; }
        public long LocalApproachFare { get; init; }
        bool localApproachFareCharged;
        public long BarCost { get; init; } = GoldPlanner.DefaultBarCost;
        public int UnexplainedDeliveryLoss { get; init; }
        public bool CorruptTravelCounter { get; init; }
        public SavedCharacter[] Roster => States.Keys.Select(n => new SavedCharacter(n)).ToArray();

        public RegionalGame(params CharacterState[] states)
        {
            foreach (var state in states)
            {
                States[state.Name] = state;
                HomeBanks[state.Name] = state.CraftBankMap;
            }
        }

        public Task LaunchAsync(SavedCharacter character, CancellationToken cancellation) => Task.CompletedTask;
        public Task<CharacterState> ReadAsync(string name, CancellationToken cancellation) => Task.FromResult(States[name]);
        public long TravelGoldSpent(string name) => TravelSpending.GetValueOrDefault(name);
        public Task WalkToBankAsync(string name, CancellationToken cancellation)
        {
            if (name == "Runner" && BankArrivalFare > 0 && !TravelSpending.ContainsKey(name))
            {
                States[name] = States[name] with { Gold = States[name].Gold - BankArrivalFare };
                TravelSpending[name] = BankArrivalFare;
            }
            States[name] = States[name] with { Map = HomeBanks[name], X = 8, Y = 8 };
            return Task.CompletedTask;
        }
        public Task ApproachCharacterAsync(string sender, string receiver, CancellationToken cancellation)
        {
            Approaches.Enqueue((sender, receiver));
            if (LocalApproachFare > 0 && !localApproachFareCharged && States[sender].Map == 10000 && States[receiver].Map == NoamBankMap)
            {
                States[sender] = States[sender] with { Gold = States[sender].Gold - LocalApproachFare };
                TravelSpending.AddOrUpdate(sender, LocalApproachFare, (_, previous) => previous + LocalApproachFare);
                localApproachFareCharged = true;
            }
            Move(sender, States[receiver].Map);
            return Task.CompletedTask;
        }
        public Task WalkToRecipientAsync(string courier, RunSettings settings, CancellationToken cancellation)
        {
            Move(courier, settings.RecipientMap);
            States[courier] = States[courier] with { Gold = States[courier].Gold - UnexplainedDeliveryLoss };
            if (CorruptTravelCounter) TravelSpending[courier] = -1;
            return Task.CompletedTask;
        }
        public Task CraftBarAsync(string name, CancellationToken cancellation)
        {
            Crafts.Enqueue(name);
            var state = States[name];
            States[name] = state with { Gold = state.Gold - BarCost, Bars = state.Bars + 1,
                BarCapacity = state.BarCapacity - 1 };
            return Task.CompletedTask;
        }
        public Task TransferGoldAsync(string sender, string receiver, long amount, CancellationToken cancellation)
        {
            Assert.InRange(amount, 1, States[sender].Gold);
            GoldTransfers.Enqueue((sender, receiver, amount));
            Assert.InRange(States[receiver].Gold + amount, 0, GoldPlanner.CarryingLimit);
            States[sender] = States[sender] with { Gold = States[sender].Gold - amount };
            States[receiver] = States[receiver] with { Gold = States[receiver].Gold + amount };
            return Task.CompletedTask;
        }
        public Task TransferBarsAsync(string sender, string receiver, int amount, CancellationToken cancellation)
        {
            var donor = States[sender];
            States[sender] = donor with { Bars = donor.Bars - amount, BarCapacity = donor.BarCapacity + amount };
            if (States.TryGetValue(receiver, out var target))
                States[receiver] = target with { Bars = target.Bars + amount, BarCapacity = target.BarCapacity - amount };
            else
            {
                Deliveries.Enqueue(amount);
            }
            return Task.CompletedTask;
        }

        void Move(string name, int destination)
        {
            var state = States[name];
            bool fromNoam = state.Map == NoamBankMap;
            bool toNoam = destination == NoamBankMap;
            if (ChargeTravel && fromNoam != toNoam)
            {
                States[name] = state = state with { Gold = state.Gold - 10_000 };
                TravelSpending.AddOrUpdate(name, 10_000, (_, previous) => previous + 10_000);
            }
            States[name] = state with { Map = destination, X = 8, Y = 8 };
        }
    }
}
