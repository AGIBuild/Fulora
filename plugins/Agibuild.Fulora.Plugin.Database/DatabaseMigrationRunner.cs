using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Agibuild.Fulora.Plugin.Database;

/// <summary>
/// Runs numbered migration scripts against the database.
/// Creates schema_version table if not exists, applies scripts in order (001_xxx.sql, 002_xxx.sql),
/// and updates schema_version after each migration.
/// </summary>
internal sealed class DatabaseMigrationRunner
{
    private readonly SqliteConnection _connection;

    /// <summary>Initializes a new instance with the specified connection.</summary>
    /// <param name="connection">The open SQLite connection to run migrations against.</param>
    public DatabaseMigrationRunner(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Applies migration scripts in version order. Skips already-applied versions.</summary>
    /// <param name="migrationScripts">Paths to SQL migration files (e.g. 001_init.sql).</param>
    public void RunMigrations(string[] migrationScripts)
    {
        EnsureSchemaVersionTable();

        var ordered = migrationScripts
            .Select(s => (Path: s, Version: ExtractVersion(s)))
            .Where(x => x.Version.HasValue)
            .OrderBy(x => x.Version!.Value)
            .ToList();

        foreach (var (path, version) in ordered)
        {
            if (!version.HasValue)
                continue;

            var currentVersion = GetCurrentVersion();
            if (version.Value <= currentVersion)
                continue;

            var script = LoadScript(path);
            if (string.IsNullOrWhiteSpace(script))
                continue;

            using var transaction = _connection.BeginTransaction();
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = script;
                cmd.Transaction = transaction;
                cmd.ExecuteNonQuery();

                using var insertCmd = _connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO schema_version (version) VALUES (@v)";
                insertCmd.Transaction = transaction;
                insertCmd.Parameters.AddWithValue("@v", version.Value);
                insertCmd.ExecuteNonQuery();

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new InvalidOperationException(
                    $"Migration failed: {Path.GetFileName(path)} (version {version}). {ex.Message}", ex);
            }
        }
    }

    private void EnsureSchemaVersionTable()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                applied_at TEXT DEFAULT (datetime('now'))
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private int GetCurrentVersion()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        var result = cmd.ExecuteScalar();
        return result is DBNull or null ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static int? ExtractVersion(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"^(\d{3})_");
        return match.Success && int.TryParse(match.Groups[1].Value, out var v) ? v : null;
    }

    private static string LoadScript(string path)
    {
        if (!File.Exists(path))
            return string.Empty;
        return File.ReadAllText(path);
    }
}
