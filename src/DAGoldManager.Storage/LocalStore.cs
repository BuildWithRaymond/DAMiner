using DAGoldManager.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DAGoldManager.Storage;

/// <summary>Serializes local configuration, durable operation history, and review acknowledgements.</summary>
/// <remarks>Passwords belong to PasswordVault and never enter this SQLite database.</remarks>
public sealed class LocalStore(string dataDirectory) : IOperationLedger
{
    readonly object gate = new();

    SqliteConnection Open()
    {
        Directory.CreateDirectory(dataDirectory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            // Short-lived connections release file handles after each operation for backup/recovery.
            DataSource = Path.Combine(dataDirectory, "gold-manager.db"), Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        // FULL synchronous writes make intent durable before the coordinator contacts the server.
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS Configuration(Id INTEGER PRIMARY KEY CHECK(Id=1), Json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Operations(Sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                OperationId TEXT NOT NULL, Outcome TEXT NOT NULL, Json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Acknowledgements(OperationId TEXT PRIMARY KEY, Time TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    public AppConfiguration Load()
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Json FROM Configuration WHERE Id=1";
            var json = command.ExecuteScalar() as string;
            // Only fresh profiles default to Local. Deserialized old profiles retain External=0.
            return json is null ? new([], new() { BankerMode = BankerMode.Local }, new()) :
                JsonSerializer.Deserialize<AppConfiguration>(json) ?? throw new InvalidDataException("Saved configuration could not be read.");
        }
    }

    public void Save(AppConfiguration configuration)
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Configuration VALUES(1,$json) ON CONFLICT(Id) DO UPDATE SET Json=$json";
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(configuration));
            command.ExecuteNonQuery();
        }
    }

    public Task AppendAsync(OperationRecord operation, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Operations(OperationId,Outcome,Json) VALUES($id,$outcome,$json)";
            command.Parameters.AddWithValue("$id", operation.OperationId.ToString());
            command.Parameters.AddWithValue("$outcome", operation.Outcome.ToString());
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(operation));
            command.ExecuteNonQuery();
        }
        return Task.CompletedTask;
    }

    // Inspect the newest outcome per operation, not older Started entries for a verified trade.
    // Acknowledgement records review separately; it never rewrites the transaction's history.
    const string UnresolvedSql = """
        SELECT o.OperationId FROM Operations o
        WHERE o.Sequence=(SELECT MAX(last.Sequence) FROM Operations last WHERE last.OperationId=o.OperationId)
          AND o.Outcome IN ('Started','NeedsReview')
          AND NOT EXISTS(SELECT 1 FROM Acknowledgements a WHERE a.OperationId=o.OperationId)
        """;

    public bool HasUnresolvedOperations()
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = UnresolvedSql;
            return command.ExecuteScalar() is not null;
        }
    }

    public void AcknowledgeUnresolvedOperations()
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT OR IGNORE INTO Acknowledgements SELECT unresolved.OperationId, $time FROM ({UnresolvedSql}) unresolved";
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<OperationRecord> RecentOperations()
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Json FROM Operations ORDER BY Sequence DESC LIMIT 100";
            using var reader = command.ExecuteReader();
            var operations = new List<OperationRecord>();
            while (reader.Read()) operations.Add(JsonSerializer.Deserialize<OperationRecord>(reader.GetString(0))!);
            return operations;
        }
    }
}
