using System.Text.Json;

namespace DAGoldManager.Updates;

internal sealed class UpdatePreferencesStore(string directory)
{
    string FilePath => Path.Combine(directory, "update-settings.json");

    public UpdatePreferences Load(UpdatePreferences fallback)
    {
        try
        {
            if (!File.Exists(FilePath)) return fallback;
            var saved = JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(FilePath));
            return saved is null ? fallback : saved with { Repository = GitHubRepository.Normalize(saved.Repository) };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return fallback;
        }
    }

    public void Save(UpdatePreferences preferences)
    {
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"update-settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
