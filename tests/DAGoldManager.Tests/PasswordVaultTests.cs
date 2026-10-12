using DAGoldManager.Storage;
using Xunit;

namespace DAGoldManager.Tests;

public class PasswordVaultTests
{
    [Fact]
    public void PasswordIsEncryptedOnDiskAndRoundTripsForCurrentWindowsUser()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "DAGoldManagerTests", Guid.NewGuid().ToString("N"));
        var vault = new PasswordVault(directory);
        vault.Save("Fictional", "synthetic-test-secret");
        Assert.True(vault.HasPassword("fictional"));
        Assert.Equal("synthetic-test-secret", vault.Read("Fictional"));
        Assert.DoesNotContain("synthetic-test-secret", System.Text.Encoding.UTF8.GetString(
            File.ReadAllBytes(Path.Combine(directory, "passwords", "FICTIONAL.bin"))));
    }
}
