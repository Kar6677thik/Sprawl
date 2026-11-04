using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Sprawl.Api.Configuration;

namespace Sprawl.Api.Data;

/// <summary>SQLite connections and schema migrations (numbered .sql files embedded in this assembly).</summary>
public sealed class Database
{
    private readonly string connectionString;
    private readonly ILogger<Database> logger;

    public Database(IOptions<ApiOptions> options, IHostEnvironment environment, ILogger<Database> logger)
    {
        this.logger = logger;
        var path = options.Value.DatabasePath;
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Api:DatabasePath is required.");
        path = Path.GetFullPath(path, environment.ContentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 10,
            Pooling = true
        }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await Execute(connection, null, "PRAGMA journal_mode = WAL; CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);", cancellationToken);
        var applied = new HashSet<int>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT version FROM schema_migrations";
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) applied.Add(reader.GetInt32(0));
        }
        var assembly = Assembly.GetExecutingAssembly();
        const string prefix = "Sprawl.Api.Data.Migrations.";
        var scripts = assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => (Name: name, Version: int.Parse(name[prefix.Length..].Split('_')[0], System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(script => script.Version);
        foreach (var script in scripts.Where(script => !applied.Contains(script.Version)))
        {
            using var stream = assembly.GetManifestResourceStream(script.Name)!;
            using var text = new StreamReader(stream);
            var sql = await text.ReadToEndAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await Execute(connection, transaction, sql, cancellationToken);
            await using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO schema_migrations (version, applied_at) VALUES ($v, $t)";
            record.Parameters.AddWithValue("$v", script.Version);
            record.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            await record.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Applied database migration {Version}", script.Version);
        }
    }

    private static async Task Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>Small helpers that keep the SQL readable.</summary>
public static class Sql
{
    public static SqliteCommand Command(this SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static async Task<int> Exec(this SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params (string, object?)[] parameters)
    {
        await using var command = connection.Command(transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string? NullableString(this SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    public static DateTimeOffset Time(this SqliteDataReader reader, int ordinal) => DateTimeOffset.Parse(reader.GetString(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    public static DateTimeOffset? NullableTime(this SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.Time(ordinal);
    public static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
