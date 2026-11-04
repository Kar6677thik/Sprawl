using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprawl.Api.Configuration;
using Sprawl.Api.Data;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Services;

/// <summary>Hourly: permanently remove canvases that have been in the trash longer than <see cref="ApiOptions.TrashDays"/>.</summary>
public sealed class TrashPurger(CanvasWriter writer, ILogger<TrashPurger> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await writer.PurgeExpired(stoppingToken); }
            catch (Exception error) when (error is not OperationCanceledException) { logger.LogWarning(error, "Trash purge failed; retrying next hour"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Development only: seed the sample canvas into an empty database, through the normal write path.</summary>
public sealed class SampleSeeder(CanvasStore store, CanvasWriter writer, IOptions<ApiOptions> options, IHostEnvironment environment, ILogger<SampleSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.SeedSample || !environment.IsDevelopment()) return;
        if ((await store.List(new CanvasFilter(IncludeArchived: true, Limit: 1), cancellationToken)).Count > 0) return;
        if ((await store.List(new CanvasFilter(Trash: true, Limit: 1), cancellationToken)).Count > 0) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Seed", "raft.json");
        if (!File.Exists(path)) { logger.LogWarning("Sample canvas file {Path} is missing", path); return; }
        await using var file = File.OpenRead(path);
        var request = await JsonSerializer.DeserializeAsync<CreateCanvasRequest>(file, SprawlJson.Options, cancellationToken)
            ?? throw new InvalidDataException("The sample canvas file is empty.");
        var result = await writer.Create(request, new Actor(ActorKinds.Mcp, "sample"), cancellationToken);
        logger.LogInformation("Seeded the sample canvas {CanvasId}", result.CanvasId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
