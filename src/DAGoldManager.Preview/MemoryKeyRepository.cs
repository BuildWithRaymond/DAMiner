using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;

namespace DAGoldManager.Preview;

// Even the framework's startup key-ring check stays in memory in this simulation host.
/// <summary>Keeps preview-host encryption keys in memory instead of writing user-profile files.</summary>
public sealed class MemoryKeyRepository : IXmlRepository
{
    readonly List<XElement> keys = [];
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (keys) return keys.Select(key => new XElement(key)).ToArray();
    }
    public void StoreElement(XElement element, string friendlyName)
    {
        lock (keys) keys.Add(new XElement(element));
    }
}
