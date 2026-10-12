using DAGoldManager.Updates;
using Xunit;

namespace DAGoldManager.Tests;

public class UpdateStartupTests
{
    [Fact]
    public void AnOverrideInsideTheApplicationFolderCannotBeReplacedByAnUpdate()
    {
        string root = Path.Combine(Path.GetTempPath(), "DAMinerUpdateTests", Guid.NewGuid().ToString("N"));
        Assert.False(StartupUpdatePolicy.DataOutsideInstall(root, Path.Combine(root, "Data")));
        Assert.False(StartupUpdatePolicy.DataOutsideInstall(root, root));
        Assert.True(StartupUpdatePolicy.DataOutsideInstall(root, root + "-data"));
    }

    [Fact]
    public void ASecondLaunchCannotApplyAnUpdateOrOwnTheSameInstallation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAMinerUpdateTests", Guid.NewGuid().ToString("N"));
        using var first = InstallationInstance.TryAcquire(directory);
        Assert.NotNull(first);
        using var second = InstallationInstance.TryAcquire(directory.ToUpperInvariant());
        Assert.Null(second);
        Assert.False(StartupUpdatePolicy.ShouldApply(false, false, true, "0.2.0", "0.3.0"));
    }

    [Theory]
    [InlineData(true, true, true, "0.3.0", false)]
    [InlineData(false, true, false, "0.3.0", false)]
    [InlineData(false, true, true, "0.1.0", false)]
    [InlineData(false, true, true, "0.2.0", false)]
    [InlineData(false, true, true, "0.3.0", true)]
    [InlineData(false, true, true, null, false)]
    public void OnlyNewerPackagesAtAnOwnedRealStartupCanApply(bool preview, bool owner, bool installed, string? pending, bool expected)
        => Assert.Equal(expected, StartupUpdatePolicy.ShouldApply(preview, owner, installed, "0.2.0", pending));
}
