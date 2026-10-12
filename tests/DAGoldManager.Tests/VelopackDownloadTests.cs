using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DAGoldManager.Updates;
using Velopack;
using Velopack.Locators;
using Xunit;

namespace DAGoldManager.Tests;

public class VelopackDownloadTests
{
    [Fact]
    public async Task RealSdkDownloadsAndVerifiesAFullPackageWithoutRunningAnyExecutable()
    {
        var (client, _) = CreateLocalFeed();
        Assert.True(client.IsInstalled);
        Assert.Equal("0.3.0", await client.CheckAsync("example/DAMiner", default));
        await client.DownloadAsync("0.3.0", _ => { }, default);
        Assert.Equal("0.3.0", client.PendingVersion);
    }

    [Fact]
    public async Task RealSdkRejectsCorruptedDownloadAndLeavesNoPendingUpdate()
    {
        var (client, package) = CreateLocalFeed();
        Assert.Equal("0.3.0", await client.CheckAsync("example/DAMiner", default));
        byte[] bytes = File.ReadAllBytes(package);
        bytes[0] ^= 1;
        File.WriteAllBytes(package, bytes);
        var failure = await Record.ExceptionAsync(() => client.DownloadAsync("0.3.0", _ => { }, default));
        Assert.NotNull(failure);
        Assert.IsType<Velopack.Exceptions.ChecksumFailedException>(failure);
        Assert.Null(client.PendingVersion);
    }

    static (VelopackUpdateClient Client, string Package) CreateLocalFeed()
    {
        string root = Path.Combine(Path.GetTempPath(), "DAMinerUpdateTests", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "feed"), cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(cache);
        string package = Path.Combine(source, "DAMiner-0.3.0-full.nupkg");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("DAMiner.nuspec").Open()))
                writer.Write("""<package><metadata><id>DAMiner</id><version>0.3.0</version><authors>Test</authors><description>Synthetic package</description></metadata></package>""");
            using var app = new StreamWriter(zip.CreateEntry("lib/net45/DAMiner.exe").Open());
            app.Write("Synthetic fixture. Never executed.");
        }
        byte[] contents = File.ReadAllBytes(package);
        var asset = new
        {
            PackageId = "DAMiner", Version = "0.3.0", Type = "Full", FileName = Path.GetFileName(package),
            SHA256 = Convert.ToHexString(SHA256.HashData(contents)), SHA1 = Convert.ToHexString(SHA1.HashData(contents)), Size = contents.Length
        };
        File.WriteAllText(Path.Combine(source, "releases.win.json"), JsonSerializer.Serialize(new { Assets = new[] { asset } }));
        var locator = new TestVelopackLocator("DAMiner", "0.2.0", cache);
        UpdateManager Manager() => new(source, new UpdateOptions { ExplicitChannel = "win" }, locator);
        return (new VelopackUpdateClient(Manager(), _ => Manager()), package);
    }
}
