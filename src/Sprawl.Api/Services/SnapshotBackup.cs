using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Sprawl.Api.Configuration;
using Sprawl.Api.Data;

namespace Sprawl.Api.Services;

/// <summary>
/// Nightly consistent copy of the database. <c>VACUUM INTO</c> reads one snapshot while writes continue, so the app
/// never stops. Each copy is integrity-checked before it is kept, then the newest <c>Keep</c> copies stay in
/// <c>Directory</c>. Getting them off the node is the job of the restic CronJob (deploy/api/backup.yaml).
/// </summary>
public sealed class SnapshotBackup(Database database, IOptions<ApiOptions> options, TimeProvider clock, IHostEnvironment environment, ILogger<SnapshotBackup> logger)
    : BackgroundService
{
    public const string Prefix = "sprawl-";
    public const string StatusFile = "backup-status.json";

    public string? Directory => string.IsNullOrWhiteSpace(options.Value.Backup.Directory)
        ? null : Path.GetFullPath(options.Value.Backup.Directory, environment.ContentRootPath);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Directory is null) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var next = new DateTimeOffset(now.Year, now.Month, now.Day, options.Value.Backup.HourUtc, 0, 0, TimeSpan.Zero);
            if (next <= now) next = next.AddDays(1);
            await Task.Delay(next - now, clock, stoppingToken);
            try { await RunOnce(stoppingToken); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Database backup failed");
                await WriteStatus("Failed", null, 0, error.GetType().Name, stoppingToken);
            }
        }
    }

    /// <summary>Make one verified copy now and prune old ones. Returns the copy's path.</summary>
    public async Task<string> RunOnce(CancellationToken cancellationToken)
    {
        var directory = Directory ?? throw new InvalidOperationException("Api:Backup:Directory is not configured.");
        System.IO.Directory.CreateDirectory(directory);
        var name = Prefix + clock.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".db";
        var final = Path.Combine(directory, name);
        var partial = final + ".partial";
        File.Delete(partial);

        await using (var connection = await database.OpenAsync(cancellationToken))
            await connection.Exec(null, "VACUUM INTO $path", cancellationToken, ("$path", partial));

        long canvases;
        await using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = partial, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            await copy.OpenAsync(cancellationToken);
            await using var check = copy.Command(null, "PRAGMA integrity_check");
            var verdict = (string?)await check.ExecuteScalarAsync(cancellationToken);
            if (verdict != "ok") throw new InvalidDataException("The backup copy failed its integrity check.");
            await using var count = copy.Command(null, "SELECT count(*) FROM canvases");
            canvases = (long)(await count.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }
        File.Move(partial, final, overwrite: true);

        var keep = options.Value.Backup.Keep;
        foreach (var old in System.IO.Directory.GetFiles(directory, Prefix + "*.db").Order(StringComparer.Ordinal).Reverse().Skip(keep))
            File.Delete(old);

        await WriteStatus("Succeeded", name, canvases, null, cancellationToken);
        logger.LogInformation("Database backup {File} written ({Canvases} canvases)", name, canvases);
        return final;
    }

    private async Task WriteStatus(string state, string? file, long canvases, string? error, CancellationToken cancellationToken)
    {
        if (Directory is not { } directory) return;
        System.IO.Directory.CreateDirectory(directory);
        var status = new { state, completedAt = clock.GetUtcNow(), file, canvases, error };
        var path = Path.Combine(directory, StatusFile);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(status), cancellationToken);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
