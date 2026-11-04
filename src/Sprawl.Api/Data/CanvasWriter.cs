using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Sprawl.Api.Configuration;
using Sprawl.Api.Events;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Data;

/// <summary>
/// The single write path. Each call is one IMMEDIATE transaction: check the version, run the domain engine, append the
/// batch and its ops, update current state (FTS follows by trigger), commit, then publish the SSE event.
/// </summary>
public sealed class CanvasWriter(Database database, CanvasEventHub events, TimeProvider clock, IIdGenerator ids, IOptions<ApiOptions> options, ILogger<CanvasWriter> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private sealed record Work(SqliteConnection Connection, SqliteTransaction Transaction, CanvasRow Row, CanvasState State, DateTimeOffset Now);

    private sealed record Outcome(EngineResult Result, long? RevertsTo = null, long? Undoes = null, bool Always = false);

    public async Task<BatchResult> Create(CreateCanvasRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ValidateRequestId(request.RequestId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction(deferred: false);
            if (request.RequestId is not null)
            {
                await using var lookup = connection.Command(transaction,
                    "SELECT result FROM batches WHERE request_id = $r AND kind = 'create' AND version = 1 LIMIT 1", ("$r", request.RequestId));
                if (await lookup.ExecuteScalarAsync(cancellationToken) is string stored)
                    return Replayed(stored);
            }
            var now = clock.GetUtcNow();
            string canvasId;
            do canvasId = ids.Next(EntityKind.Canvas);
            while (await Rows.Canvas(connection, transaction, canvasId, cancellationToken) is not null);
            var result = BatchEngine.Create(canvasId, request, actor, now, ids);
            var meta = (CanvasMeta)result.Ops.First(op => op.EntityId == canvasId).After!;
            await connection.Exec(transaction,
                "INSERT INTO canvases (id, title, description, tags, collection, pinned, archived, version, created_at, updated_at) VALUES ($id, $t, $d, $tags, $c, 0, 0, 0, $now, $now)",
                cancellationToken, ("$id", canvasId), ("$t", meta.Title), ("$d", meta.Description), ("$tags", JsonSerializer.Serialize(meta.Tags)), ("$c", meta.Collection), ("$now", Sql.Iso(now)));
            var row = new CanvasRow(meta, 0, null, now);
            var batch = await Persist(new Work(connection, transaction, row, new CanvasState(), now), new Outcome(result, Always: true), BatchKinds.Create, actor, request.RequestId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            Publish(canvasId, batch.Event);
            logger.LogInformation("Created canvas {CanvasId} ({Actor})", canvasId, actor.Kind);
            return batch.Result;
        }
        finally { gate.Release(); }
    }

    public Task<BatchResult> Batch(string canvasId, BatchRequest request, long? expectedVersion, Actor actor, CancellationToken cancellationToken) =>
        Commit(canvasId, expectedVersion, request.RequestId, actor, request.Kind ?? BatchKinds.Edit, cancellationToken,
            work => Task.FromResult(new Outcome(BatchEngine.Execute(work.State, request, actor, work.Now, ids))));

    public Task<BatchResult> Undo(string canvasId, long version, long? expectedVersion, Actor actor, CancellationToken cancellationToken) =>
        Commit(canvasId, expectedVersion, null, actor, BatchKinds.Undo, cancellationToken, async work =>
        {
            if (version < 2 || version > work.Row.Version) throw SprawlException.Invalid($"Undo a version between 2 and {work.Row.Version}.");
            var ops = await CanvasStore.Ops(work.Connection, work.Transaction, canvasId, version, version, cancellationToken);
            var undo = HistoryEngine.Undo(work.State, ops);
            var after = HistoryEngine.Replay(work.State, undo);
            return new Outcome(new EngineResult(undo, [], [], [], $"Undid version {version}: " + Summaries.Describe(undo, work.State, after, BatchKinds.Undo)), Undoes: version);
        });

    public Task<BatchResult> Revert(string canvasId, long toVersion, long expectedVersion, Actor actor, CancellationToken cancellationToken) =>
        Commit(canvasId, expectedVersion, null, actor, BatchKinds.Revert, cancellationToken, async work =>
        {
            if (toVersion < 1 || toVersion >= work.Row.Version) throw SprawlException.Invalid($"Revert to a version between 1 and {work.Row.Version - 1}.");
            await Snapshot(work.Connection, work.Transaction, canvasId, work.Row.Version, work.State, work.Now, cancellationToken);
            var (target, _) = await CanvasStore.StateAt(work.Connection, work.Transaction, canvasId, toVersion, cancellationToken);
            var ops = HistoryEngine.Revert(work.State, target);
            return new Outcome(new EngineResult(ops, [], [], [], $"Reverted to version {toVersion}: " + Summaries.Describe(ops, work.State, target, BatchKinds.Revert)), RevertsTo: toVersion);
        });

    public Task<BatchResult> Trash(string canvasId, long expectedVersion, Actor actor, CancellationToken cancellationToken) =>
        Commit(canvasId, expectedVersion, null, actor, BatchKinds.Trash, cancellationToken, async work =>
        {
            await work.Connection.Exec(work.Transaction, "UPDATE canvases SET deleted_at = $now WHERE id = $id", cancellationToken, ("$now", Sql.Iso(work.Now)), ("$id", canvasId));
            return new Outcome(new EngineResult([], [], [], [], $"Moved to trash (restorable for {options.Value.TrashDays} days)"), Always: true);
        });

    public Task<BatchResult> Restore(string canvasId, Actor actor, CancellationToken cancellationToken) =>
        Commit(canvasId, null, null, actor, BatchKinds.Restore, cancellationToken, async work =>
        {
            if (work.Row.DeletedAt is null) throw SprawlException.Invalid("The canvas is not in the trash.");
            await work.Connection.Exec(work.Transaction, "UPDATE canvases SET deleted_at = NULL WHERE id = $id", cancellationToken, ("$id", canvasId));
            return new Outcome(new EngineResult([], [], [], [], "Restored from trash"), Always: true);
        }, allowTrashed: true);

    /// <summary>Permanently remove a canvas with its history. Cascades to every table; FTS follows by trigger.</summary>
    public async Task Purge(string canvasId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            if (await connection.Exec(null, "DELETE FROM canvases WHERE id = $id", cancellationToken, ("$id", canvasId)) == 0)
                throw SprawlException.NotFound($"Canvas '{canvasId}' was not found.");
            logger.LogInformation("Purged canvas {CanvasId}", canvasId);
        }
        finally { gate.Release(); }
    }

    public async Task<int> PurgeExpired(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            var cutoff = clock.GetUtcNow().AddDays(-options.Value.TrashDays);
            var purged = await connection.Exec(null, "DELETE FROM canvases WHERE deleted_at IS NOT NULL AND deleted_at < $cutoff", cancellationToken, ("$cutoff", Sql.Iso(cutoff)));
            if (purged > 0) logger.LogInformation("Purged {Count} canvases from the trash", purged);
            return purged;
        }
        finally { gate.Release(); }
    }

    private async Task<BatchResult> Commit(string canvasId, long? expectedVersion, string? requestId, Actor actor, string kind, CancellationToken cancellationToken,
        Func<Work, Task<Outcome>> work, bool allowTrashed = false)
    {
        ValidateRequestId(requestId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction(deferred: false);
            var row = await Rows.Canvas(connection, transaction, canvasId, cancellationToken);
            if (row is null || (row.DeletedAt is not null && !allowTrashed)) throw SprawlException.NotFound($"Canvas '{canvasId}' was not found.");
            if (requestId is not null)
            {
                await using var lookup = connection.Command(transaction, "SELECT result FROM batches WHERE canvas_id = $c AND request_id = $r", ("$c", canvasId), ("$r", requestId));
                if (await lookup.ExecuteScalarAsync(cancellationToken) is string stored) return Replayed(stored);
            }
            if (expectedVersion is { } expected && expected != row.Version) throw SprawlException.Stale(row.Version);
            var state = await Rows.State(connection, transaction, row.Meta, cancellationToken);
            var context = new Work(connection, transaction, row, state, clock.GetUtcNow());
            var outcome = await work(context);
            if (outcome.Result.Ops.Count == 0 && !outcome.Always)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(canvasId, row.Version, false, outcome.Result.Summary, outcome.Result.Refs, outcome.Result.Skipped, outcome.Result.Warnings);
            }
            var batch = await Persist(context, outcome, kind, actor, requestId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            Publish(canvasId, batch.Event);
            return batch.Result;
        }
        finally { gate.Release(); }
    }

    private async Task<(BatchResult Result, CanvasEvent Event)> Persist(Work work, Outcome outcome, string kind, Actor actor, string? requestId, CancellationToken cancellationToken)
    {
        var (connection, transaction, row, state, now) = work;
        var canvasId = row.Meta.Id;
        var version = row.Version + 1;
        var result = outcome.Result;
        var batchResult = new BatchResult(canvasId, version, true, result.Summary, result.Refs, result.Skipped, result.Warnings);
        await connection.Exec(transaction,
            "INSERT INTO batches (canvas_id, version, kind, actor, client, summary, request_id, reverts_to, undoes, op_count, result, created_at) " +
            "VALUES ($c, $v, $kind, $actor, $client, $summary, $request, $reverts, $undoes, $count, $result, $now)", cancellationToken,
            ("$c", canvasId), ("$v", version), ("$kind", kind), ("$actor", actor.Kind), ("$client", actor.Client), ("$summary", result.Summary), ("$request", requestId),
            ("$reverts", outcome.RevertsTo), ("$undoes", outcome.Undoes), ("$count", result.Ops.Count), ("$result", CanvasStore.Serialize(batchResult)), ("$now", Sql.Iso(now)));
        for (var seq = 0; seq < result.Ops.Count; seq++)
        {
            var op = result.Ops[seq];
            await connection.Exec(transaction,
                "INSERT INTO ops (canvas_id, version, seq, type, entity_id, before_json, after_json) VALUES ($c, $v, $s, $type, $entity, $before, $after)", cancellationToken,
                ("$c", canvasId), ("$v", version), ("$s", seq), ("$type", op.Type), ("$entity", op.EntityId), ("$before", SprawlJson.Entity(op.Before)), ("$after", SprawlJson.Entity(op.After)));
        }
        await Rows.Apply(connection, transaction, canvasId, result.Ops, cancellationToken);
        await connection.Exec(transaction, "UPDATE canvases SET version = $v, updated_at = $now WHERE id = $id", cancellationToken, ("$v", version), ("$now", Sql.Iso(now)), ("$id", canvasId));
        if (version % options.Value.SnapshotEvery == 0)
            await Snapshot(connection, transaction, canvasId, version, HistoryEngine.Replay(state, result.Ops), now, cancellationToken);
        var entry = new HistoryEntry(version, kind, actor.Kind, actor.Client, result.Summary, outcome.RevertsTo, outcome.Undoes, result.Ops.Count, now);
        return (batchResult, CanvasStore.ToEvent(canvasId, entry, result.Ops));
    }

    private static async Task Snapshot(SqliteConnection connection, SqliteTransaction transaction, string canvasId, long version, CanvasState state, DateTimeOffset now, CancellationToken cancellationToken) =>
        await connection.Exec(transaction, "INSERT OR IGNORE INTO snapshots (canvas_id, version, state, created_at) VALUES ($c, $v, $s, $now)", cancellationToken,
            ("$c", canvasId), ("$v", version), ("$s", SprawlJson.Compress(state.ToSnapshot())), ("$now", Sql.Iso(now)));

    private void Publish(string canvasId, CanvasEvent canvasEvent)
    {
        try { events.Publish(canvasId, canvasEvent); }
        catch (Exception error) { logger.LogWarning(error, "Publishing the event for {CanvasId} v{Version} failed", canvasId, canvasEvent.Version); }
    }

    private static BatchResult Replayed(string stored) =>
        (JsonSerializer.Deserialize<BatchResult>(stored, SprawlJson.Options) ?? throw new InvalidDataException("Stored batch result is empty.")) with { Replayed = true };

    private static void ValidateRequestId(string? requestId)
    {
        if (requestId is not null && (requestId.Length is 0 or > 100 || requestId.Any(char.IsControl)))
            throw SprawlException.Invalid("requestId must be 1-100 printable characters.");
    }
}
