using System.Text.RegularExpressions;

namespace DAGoldManager.Updates;

/// <summary>Accepts public owner/repository names or GitHub URLs, never tokens or other hosts.</summary>
public static class GitHubRepository
{
    public static string Normalize(string value)
    {
        string repository = value.Trim();
        if (repository.Length == 0) return "";
        if (repository.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(repository, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort ||
                uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new ArgumentException("Use a public GitHub owner/repository, without credentials or query parameters.");
            repository = uri.AbsolutePath.Trim('/');
        }
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repository = repository[..^4];
        string[] parts = repository.Split('/');
        if (parts.Length != 2 || !Regex.IsMatch(parts[0], @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$") ||
            !Regex.IsMatch(parts[1], @"^[A-Za-z0-9_.-]{1,100}$") || parts[1] is "." or "..")
            throw new ArgumentException("Enter GitHub owner/repository, for example YourGitHubName/DAMiner.");
        return repository;
    }
}
