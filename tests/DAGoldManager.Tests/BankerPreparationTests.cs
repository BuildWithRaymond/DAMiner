using System.Collections.Immutable;
using DAGoldManager.Core;
using DAGoldManager.Game;
using DAQuester.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class BankerPreparationTests
{
    static Entity Lamont => new(42, "Lamont", new(3, 4), true);
    static CharacterSnapshot State(int map, params Entity[] entities) => new()
    {
        Connected = true, Name = "Banker", Id = 7, Generation = 1, Revision = 10,
        Map = map, Position = new(6, 8), Entities = entities.ToImmutableDictionary(e => e.Id)
    };

    [Fact]
    public async Task BankerNavigatesToSafeAbelFloorEvenWhenLamontIsVisibleOnAnotherMap()
    {
        var state = State(901, Lamont);
        var commands = new List<GameCommand>();
        await BankerPreparation.PrepareAsync(() => state, (command, _) =>
        {
            commands.Add(command);
            state = State(command.Map!.Value, Lamont);
            return Task.CompletedTask;
        }, _ => throw new InvalidOperationException("NPC is already visible after arrival."), default);
        var arrival = Assert.Single(commands);
        Assert.Equal("navigate", arrival.Kind);
        Assert.Equal(167, arrival.Map);
        Assert.Equal(new Tile(6, 8), arrival.Target);
        Assert.Equal(8, arrival.TargetRow);
        Assert.Equal(42u, BankerPreparation.RequireNpc(state).Id);
    }

    [Fact]
    public async Task WaitsForLamontAfterArrivalBeforeFinishingPreparation()
    {
        var state = State(901);
        bool waited = false;
        await BankerPreparation.PrepareAsync(() => state, (_, _) =>
        {
            state = State(167);
            return Task.CompletedTask;
        }, _ =>
        {
            waited = true;
            state = State(167, Lamont);
            return Task.CompletedTask;
        }, default);
        Assert.True(waited);
        Assert.True(BankerPreparation.NpcVisible(state));
    }

    [Fact]
    public void RejectsWrongMapMissingOrAmbiguousDepositNpc()
    {
        Assert.Throws<InvalidOperationException>(() => BankerPreparation.RequireNpc(State(901, Lamont)));
        Assert.Throws<InvalidOperationException>(() => BankerPreparation.RequireNpc(State(167)));
        Assert.Throws<InvalidOperationException>(() => BankerPreparation.RequireNpc(State(167, Lamont, Lamont with { Id = 43 })));
        Assert.Throws<InvalidOperationException>(() => BankerPreparation.RequireNpc(State(167, Lamont with { IsPlayer = true })));
    }

    [Fact]
    public async Task SimulationMovesBankerToAbelFloorAndIgnoresOldExternalDestination()
    {
        var game = new SimulatedGame([new("Banker")]);
        var settings = new RunSettings { BankerMode = BankerMode.Local, Recipient = "Banker", RecipientMap = 901, RecipientX = 19, RecipientY = 12 };
        await game.WalkToRecipientAsync("Banker", settings with { BankerMode = BankerMode.External }, default);
        Assert.Equal(901, (await game.ReadAsync("Banker", default)).Map);
        await game.PrepareBankerAsync("Banker", settings, default);
        var state = await game.ReadAsync("Banker", default);
        Assert.Equal(167, state.Map);
        Assert.Equal(6, state.X);
        Assert.Equal(8, state.Y);
    }
}
