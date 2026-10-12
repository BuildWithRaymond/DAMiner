using System.Collections.Immutable;
using DAGoldManager.Core;
using DAGoldManager.Game;
using DAQuester.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class SetupRecorderTests
{
    static CharacterSnapshot Before => new()
    {
        Generation = 10, Revision = 1, Name = "Recorder", Connected = true, Gold = 99_999_999,
        InventoryKnown = true, Map = 167, Position = new(10, 10)
    };

    [Fact]
    public void RecipeRequiresBothPaidGoldAndOneNewBar()
    {
        var recorder = new SetupRecorder();
        recorder.BeginNpc(Before, new(42, "Banker", new(5, 6), true));
        recorder.RecordAction(new(DialogActionKind.Choice, "Bank menu", "Make gold bar"));
        recorder.ObserveState(Before with { Revision = 2, Gold = 49_499_999 });
        Assert.Null(recorder.Recipe);
        recorder.ObserveState(Before with { Revision = 3, Gold = 49_499_999,
            Inventory = ImmutableDictionary<byte, Item>.Empty.Add(1, new(1, "Gold Bar", 1)) });
        Assert.NotNull(recorder.Recipe);
        Assert.Equal(50_500_000, recorder.Recipe.GoldCost);
        Assert.Equal(167, recorder.Recipe.Map);
        Assert.Equal("Gold Bar", recorder.Recipe.BarName);
        Assert.Single(recorder.Recipe.Actions);
    }

    [Fact]
    public void ReplacementConnectionCannotFinishOldRecipe()
    {
        var recorder = new SetupRecorder();
        recorder.BeginNpc(Before, new(42, "Banker", new(5, 6), true));
        recorder.RecordAction(new(DialogActionKind.Choice, "Bank menu", "Make gold bar"));
        recorder.ObserveState(Before with { Generation = 11, Gold = 49_499_999,
            Inventory = ImmutableDictionary<byte, Item>.Empty.Add(1, new(1, "Gold Bar", 1)) });
        Assert.Null(recorder.Recipe);
    }

    [Fact]
    public void RecorderRejectsNonnumericTextInputs()
    {
        var recorder = new SetupRecorder();
        recorder.BeginNpc(Before, new(42, "Banker", new(5, 6), true));
        Assert.Throws<InvalidOperationException>(() => recorder.RecordAction(new(DialogActionKind.Input, "Amount?", "a password")));
    }

    [Fact]
    public void LargestVerifiedSampleDefinesUsableBatchSize()
    {
        var recorder = new SetupRecorder();
        recorder.RecordVerifiedTrade(3);
        recorder.RecordVerifiedTrade(2);
        Assert.Equal(3, recorder.VerifiedBatchSize);
    }

    [Fact]
    public void ObservedWaitNoticeUpdatesCooldownHint()
    {
        var recorder = new SetupRecorder();
        recorder.BeginNpc(Before, new(42, "Banker", new(5, 6), true));
        recorder.RecordAction(new(DialogActionKind.Choice, "Bank menu", "Make gold bar"));
        recorder.ObserveState(Before with { Revision = 3, Gold = 49_499_999,
            Inventory = ImmutableDictionary<byte, Item>.Empty.Add(1, new(1, "Gold Bar", 1)) });
        recorder.RecordCooldownNotice("Please wait 45 seconds before making another gold bar.");
        Assert.Equal(45, recorder.Recipe!.CooldownSeconds);
    }

    [Fact]
    public void VerifiedTradeLearnsDecoratedDisplayNameWithoutChangingInventoryIdentity()
    {
        var recorder = new SetupRecorder();
        recorder.BeginNpc(Before, new(42, "Banker", new(5, 6), true));
        recorder.RecordAction(new(DialogActionKind.Choice, "Bank menu", "Make gold bar"));
        recorder.ObserveState(Before with { Revision = 3, Gold = 49_499_999,
            Inventory = ImmutableDictionary<byte, Item>.Empty.Add(1, new(1, "Gold Bar", 1)) });
        recorder.RecordVerifiedTrade(3, "Gold Bar 100%");
        Assert.Equal("Gold Bar", recorder.Recipe!.BarName);
        Assert.Equal("Gold Bar 100%", recorder.Recipe.OfferName);
    }
}
