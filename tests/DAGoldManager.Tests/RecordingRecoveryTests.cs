using DAGoldManager.Core;
using DAGoldManager.Game;
using DAGoldManager.Storage;
using DAGoldManager.UI;
using Xunit;

namespace DAGoldManager.Tests;

public class RecordingRecoveryTests
{
    static CraftRecipe Recipe => new("Fictional banker", 167, 3, 4, "Gold Bar", 50_500_000,
        [new(DialogActionKind.Choice, "Synthetic menu", "Buy Gold Bar")], DateTimeOffset.UtcNow);

    static (DashboardModel Model, LocalStore Store, string Directory) OpenDraft(int batch, string? error = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAGoldManagerTests", Guid.NewGuid().ToString("N"));
        var store = new LocalStore(directory);
        store.Save(new([new("Fictional")], new() { Courier = "Fictional" }, new(),
            Draft: new("Fictional", Recipe, batch, error)));
        return (new(false, directory, ""), store, directory);
    }

    [Fact]
    public void CompleteRecordingCanBeSavedAfterStoppingAndReopening()
    {
        var (model, store, _) = OpenDraft(1);
        model.StopRecording();
        Assert.False(model.Recording);
        Assert.True(model.CanSaveRecording);
        model.SaveRecording();
        var saved = store.Load();
        Assert.Equal("Fictional banker", saved.Recipe!.NpcName);
        Assert.Equal(1, saved.Run.BarsPerTrade);
        Assert.Null(saved.Draft);
        Assert.False(model.CanSaveRecording);
    }

    [Fact]
    public void CraftOnlyDraftSurvivesRestartWithoutEnablingGoOrPretendingTradeWasVerified()
    {
        var (_, _, directory) = OpenDraft(0);
        var reopened = new DashboardModel(false, directory, "");
        Assert.NotNull(reopened.RecordedRecipe);
        Assert.Equal("Fictional banker", reopened.RecordedRecipe.NpcName);
        Assert.Null(reopened.Recipe);
        Assert.Equal(0, reopened.RecordedBatchSize);
        Assert.False(reopened.CanSaveRecording);
    }

    [Fact]
    public void ErrorInCompleteDraftStillBlocksSaving()
    {
        var (model, _, _) = OpenDraft(1, "Synthetic interrupted recording");
        Assert.False(model.CanSaveRecording);
        Assert.Contains("Synthetic interrupted", model.RecordingError);
    }

    [Fact]
    public void ResumingRecorderRetainsVerifiedCraftAndBatch()
    {
        var recipe = Recipe with { OfferName = "Gold Bar 100%" };
        var recorder = new SetupRecorder(recipe, 1);
        Assert.Equal(recipe, recorder.Recipe);
        Assert.Equal(1, recorder.VerifiedBatchSize);
    }
}
