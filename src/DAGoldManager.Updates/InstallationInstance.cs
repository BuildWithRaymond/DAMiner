using System.Security.Cryptography;
using System.Text;

namespace DAGoldManager.Updates;

/// <summary>Owns a per-install mutex so a second app launch cannot update an active installation.</summary>
public sealed class InstallationInstance : IDisposable
{
    readonly Mutex mutex;
    InstallationInstance(Mutex mutex) => this.mutex = mutex;

    public static InstallationInstance? TryAcquire(string directory)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        var mutex = new Mutex(initiallyOwned: true, $"DAMiner.{key}", out bool created);
        if (created) return new(mutex);
        mutex.Dispose();
        return null;
    }

    public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
}
