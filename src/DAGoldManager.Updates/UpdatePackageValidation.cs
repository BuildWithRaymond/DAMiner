using Velopack;

namespace DAGoldManager.Updates;

/// <summary>Rejects unexpected package identities, filenames, sizes, and incomplete checksum metadata.</summary>
public static class UpdatePackageValidation
{
    public static void Validate(VelopackAsset asset)
    {
        if (!string.Equals(asset.PackageId, "DAMiner", StringComparison.OrdinalIgnoreCase) || asset.Type != VelopackAssetType.Full)
            throw new InvalidDataException("Release does not contain a full DAMiner package.");
        if (asset.SHA256 is not { Length: 64 } || !asset.SHA256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Release must include a SHA-256 package checksum.");
        if (asset.Size is <= 0 or > 1L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Update package size is invalid.");
        if (string.IsNullOrWhiteSpace(asset.FileName) || asset.FileName.IndexOfAny(['/', '\\', ':']) >= 0 ||
            !asset.FileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update package filename is invalid.");
    }
}
