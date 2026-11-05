using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Sprawl.Api.Services;
using Sprawl.Contracts;

namespace Sprawl.Api.Tests;

public sealed class BackupTests
{
    [Fact]
    public async Task Backups_are_consistent_verified_copies_and_old_ones_are_pruned()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sprawl-backup-tests-" + Guid.NewGuid().ToString("N"));
        using var factory = new SprawlFactory();
        factory.Settings["Api:Backup:Directory"] = directory;
        factory.Settings["Api:Backup:Keep"] = "2";
        try
        {
            var device = factory.Device();
            var created = await (await device.PostAsJsonAsync("/api/v1/canvases",
                new CreateCanvasRequest("Backed up", Intents: [new AddCardIntent(CardKinds.Idea, "Survives a restore", "R")]), Api.Json)).Read<BatchResult>();
            var backup = factory.Services.GetRequiredService<SnapshotBackup>();

            var paths = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                factory.Clock.Advance(TimeSpan.FromMinutes(1));
                paths.Add(await backup.RunOnce(CancellationToken.None));
            }

            // Only the newest two copies are kept; no partial files are left behind.
            Assert.Equal(paths[1..].Order(), Directory.GetFiles(directory, "sprawl-*.db").Order());
            Assert.Empty(Directory.GetFiles(directory, "*.partial"));

            // A copy is a complete, working database: the canvas and its card are there.
            await using (var copy = new SqliteConnection($"Data Source={paths[2]};Mode=ReadOnly;Pooling=False"))
            {
                await copy.OpenAsync();
                await using var command = copy.CreateCommand();
                command.CommandText = "SELECT c.title FROM cards c JOIN canvases v ON v.id = c.canvas_id WHERE v.id = $id";
                command.Parameters.AddWithValue("$id", created.CanvasId);
                Assert.Equal("Survives a restore", await command.ExecuteScalarAsync());
            }

            // The owner can take one on demand; the MCP's device principal cannot.
            var owner = await factory.Owner();
            factory.Clock.Advance(TimeSpan.FromMinutes(1));
            var made = await (await owner.PostAsync("/api/v1/backups", null)).Read<JsonElement>();
            paths.Add(Path.Combine(directory, made.GetProperty("file").GetString()!));
            Assert.True(File.Exists(paths[^1]));
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await device.PostAsync("/api/v1/backups", null)).StatusCode);

            var status = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, SnapshotBackup.StatusFile))).RootElement;
            Assert.Equal("Succeeded", status.GetProperty("state").GetString());
            Assert.Equal(1, status.GetProperty("canvases").GetInt32());
            Assert.Equal(Path.GetFileName(paths[3]), status.GetProperty("file").GetString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
