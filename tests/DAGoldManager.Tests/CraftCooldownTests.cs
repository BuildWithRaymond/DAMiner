using DAGoldManager.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class CraftCooldownTests
{
    [Fact]
    public void RecentlyRecordedCraftDelaysOnlyThatCharacter()
    {
        var cooldowns = new CraftCooldowns();
        var start = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
        cooldowns.RecordCraft("Recorder", start);
        Assert.Equal(TimeSpan.FromSeconds(20), cooldowns.Remaining("recorder", start.AddSeconds(10), 30));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining("Other", start.AddSeconds(10), 30));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining("Recorder", start.AddSeconds(31), 30));
    }
}
