using Microsoft.Data.Sqlite;

namespace Agibuild.Fulora.Plugin.Database;

/// <summary>
/// SQLite-backed implementation of <see cref="IDatabaseService"/>.
/// Thread-safe via a lock on all operations.
/// </summary>
public sealed class DatabaseService : IDatabaseService, IDisposable
{
    private readonly string _connectionString;
    private readonly string[]? _migrationScripts;
    private readonly object _lock = new();
    private SqliteConnection? _connection;
    private SqliteTransaction? _activeTransaction;

    /// <summary>Initializes a new instance with the specified options.</summary>
    /// <param name="options">Database configuration. Uses defaults when null.</param>
    public DatabaseService(DatabaseOptions? options = null)
    {
        var opts = options ?? new DatabaseOptions();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = opts.DatabasePath,
            Mode = opts.DatabasePath == ":memory:"
                ? SqliteOpenMode.Memory
                : SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        _migrationScripts = opts.MigrationScripts;
    }

    /// <summary>Executes a SELECT query and returns rows as dictionaries.</summary>
    public Task<QueryResult> Query(string sql, Dictionary<string, object?>? parameters = null)
    {
        lock (_lock)
        {
            return QueryCore(sql, parameters);
        }
    }

    /// <summary>Executes a non-query statement and returns the number of affected rows.</summary>
    public Task<int> Execute(string sql, Dictionary<string, object?>? parameters = null)
    {
        lock (_lock)
        {
            return ExecuteCore(sql, parameters);
        }
    }

    /// <summary>Executes multiple statements in a single transaction.</summary>
    public Task<int> ExecuteBatch(string[] statements)
    {
        lock (_lock)
        {
            return ExecuteBatchCore(statements);
        }
    }

    /// <summary>Begins a new transaction. Must call Commit or Rollback before starting another.</summary>
    public Task BeginTransaction()
    {
        lock (_lock)
        {
            if (_activeTransaction != null)
                throw new InvalidOperationException("A transaction is already active. Commit or rollback before starting a new one.");
            EnsureConnection();
            _activeTransaction = _connection!.BeginTransaction();
            return Task.CompletedTask;
        }
    }

    /// <summary>Commits the active transaction.</summary>
    public Task CommitTransaction()
    {
        lock (_lock)
        {
            if (_activeTransaction == null)
                throw new InvalidOperationException("No active transaction to commit.");
            _activeTransaction.Commit();
            _activeTransaction.Dispose();
            _activeTransaction = null;
            return Task.CompletedTask;
        }
    }

    /// <summary>Rolls back the active transaction.</summary>
    public Task RollbackTransaction()
    {
        lock (_lock)
        {
            if (_activeTransaction == null)
                throw new InvalidOperationException("No active transaction to rollback.");
            _activeTransaction.Rollback();
            _activeTransaction.Dispose();
            _activeTransaction = null;
            return Task.CompletedTask;
        }
    }

    /// <summary>Returns the current schema version from applied migrations.</summary>
    public Task<int> GetSchemaVersion()
    {
        lock (_lock)
        {
            return Task.FromResult(GetSchemaVersionCore());
        }
    }

    private void EnsureConnection()
    {
        if (_connection != null)
            return;

        var dataSource = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        var dir = Path.GetDirectoryName(dataSource);
        if (dir is not null && !string.IsNullOrEmpty(dir) && dir != ":memory:" && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        EnsureSchemaVersionTable();

        if (_migrationScripts is { Length: > 0 })
        {
            var runner = new DatabaseMigrationRunner(_connection);
            runner.RunMigrations(_migrationScripts);
        }
    }

    private void EnsureSchemaVersionTable()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                applied_at TEXT DEFAULT (datetime('now'))
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private int GetSchemaVersionCore()
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        var result = cmd.ExecuteScalar();
        return result is DBNull or null ? 0 : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<QueryResult> QueryCore(string sql, Dictionary<string, object?>? parameters)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _activeTransaction;
        BindParameters(cmd, parameters);

        var columns = Array.Empty<string>();
        var rows = new List<Dictionary<string, object?>>();

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (reader.FieldCount > 0)
            {
                columns = Enumerable.Range(0, reader.FieldCount)
                    .Select(reader.GetName)
                    .ToArray();
            }

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[name] = value;
                }
                rows.Add(row);
            }
        }

        return new QueryResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
        };
    }

    private async Task<int> ExecuteCore(string sql, Dictionary<string, object?>? parameters)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _activeTransaction;
        BindParameters(cmd, parameters);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> ExecuteBatchCore(string[] statements)
    {
        EnsureConnection();
        var totalAffected = 0;
        using var transaction = _connection!.BeginTransaction();
        try
        {
            foreach (var sql in statements)
            {
                if (string.IsNullOrWhiteSpace(sql))
                    continue;
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Transaction = transaction;
                totalAffected += await cmd.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        return totalAffected;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activeTransaction?.Dispose();
        _activeTransaction = null;
        _connection?.Dispose();
        _connection = null;
    }

    private static void BindParameters(SqliteCommand cmd, Dictionary<string, object?>? parameters)
    {
        if (parameters == null)
            return;

        foreach (var (key, value) in parameters)
        {
            var paramName = key.StartsWith('@') ? key : "@" + key;
            var param = cmd.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
            if (value is long or int or short or byte)
                param.SqliteType = SqliteType.Integer;
            else if (value is double or float)
                param.SqliteType = SqliteType.Real;
            else if (value is DateTime or DateTimeOffset)
                param.SqliteType = SqliteType.Text;
        }
    }
}
