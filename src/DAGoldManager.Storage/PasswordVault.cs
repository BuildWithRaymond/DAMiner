namespace DAGoldManager.Storage;

/// <summary>Stores per-character credentials using DPAPI for the current Windows user.</summary>
public sealed class PasswordVault(string dataDirectory)
{
    string FileName(string name)
    {
        // Restrict names before building paths; a character name must never escape the vault folder.
        if (name.Length is < 1 or > 16 || !name.All(char.IsAsciiLetter))
            throw new InvalidOperationException("Character names must contain 1–16 letters.");
        return Path.Combine(dataDirectory, "passwords", name.ToUpperInvariant() + ".bin");
    }

    public bool HasPassword(string name) => File.Exists(FileName(name));

    public void Save(string name, string password)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Passwords use Windows encryption.");
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Enter a password to save.");
        string path = FileName(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(password);
        try
        {
            byte[] encrypted = System.Security.Cryptography.ProtectedData.Protect(bytes, null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            // Replace only after encryption and a complete write, preserving an existing saved file.
            File.WriteAllBytes(path + ".tmp", encrypted);
            File.Move(path + ".tmp", path, true);
        }
        // Clear the temporary plaintext buffer; the caller still owns its original string.
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    public string Read(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Passwords use Windows encryption.");
        byte[] bytes = System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(FileName(name)), null,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        try { return System.Text.Encoding.UTF8.GetString(bytes); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
