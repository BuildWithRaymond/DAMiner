using DAQuester.DarkAges.Protocol;
using Xunit;

namespace DAGoldManager.Tests;

public class ObserverHookTests
{
    [Fact]
    public void ExchangePacketsReachObserversWithoutInventingCharacterState()
    {
        var tracker = new StateTracker(1);
        tracker.Identity("Fictional", 1);
        long revision = tracker.Snapshot.Revision;
        bool observed = false;
        tracker.Observed += (opcode, _) => observed = opcode == 0x42;
        tracker.Observe(0x42, [5, 2]);
        Assert.True(observed);
        Assert.Equal(revision, tracker.Snapshot.Revision);
    }
}
