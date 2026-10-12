using DAGoldManager.Updates;
using Velopack;
using Xunit;

namespace DAGoldManager.Tests;

public class UpdatePackageValidationTests
{
    static VelopackAsset Asset() => new()
    {
        PackageId = "DAMiner", Type = VelopackAssetType.Full, FileName = "DAMiner-0.3.0-full.nupkg", Size = 123,
        SHA256 = new string('a', 64)
    };

    [Fact]
    public void AcceptsOnlyBoundedFullPackagesWithSha256()
        => UpdatePackageValidation.Validate(Asset());

    [Fact]
    public void RejectsAnotherApplicationMissingChecksumOrUnsafePackagePath()
    {
        var other = Asset(); other.PackageId = "AnotherApp";
        Assert.Throws<InvalidDataException>(() => UpdatePackageValidation.Validate(other));
        var unhashed = Asset(); unhashed.SHA256 = "";
        Assert.Throws<InvalidDataException>(() => UpdatePackageValidation.Validate(unhashed));
        var unsafePath = Asset(); unsafePath.FileName = "../update.nupkg";
        Assert.Throws<InvalidDataException>(() => UpdatePackageValidation.Validate(unsafePath));
        var huge = Asset(); huge.Size = 2L * 1024 * 1024 * 1024;
        Assert.Throws<InvalidDataException>(() => UpdatePackageValidation.Validate(huge));
    }
}
