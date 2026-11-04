using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Data;

public sealed record CanvasFilter(string? Tag = null, string? Collection = null, bool IncludeArchived = false, string? Query = null, bool Trash = false, int Limit = 50);

/// <summary>Read side: lists, documents, outlines, history, past states and search. Trashed canvases are invisible unless asked for.</summary>
public sealed partial class CanvasStore(Database database)
{
    public async Task<List<CanvasSummary>> List(CanvasFilter filter, CancellationToken cancellationToken)
    {
        var where = new List<string> { filter.Trash ? "v.deleted_at IS NOT NULL" : "v.deleted_at IS NULL" };
        var parameters = new List<(string, object?)> { ("$limit", Math.Clamp(filter.Limit, 1, 100)) };
        if (!filter.IncludeArchived && !filter.Trash) where.Add("v.archived = 0");
        if (filter.Tag is { Length: > 0 } tag)
        {
            where.Add("EXISTS (SELECT 1 FROM json_each(v.tags) WHERE json_each.value = $tag)");
            parameters.Add(("$tag", Text.Tags([tag]).FirstOrDefault() ?? tag));
        }
        if (filter.Collection is { Length: > 0 } collection)
        {
            where.Add("v.collection = $collection COLLATE NOCASE");
            parameters.Add(("$collection", collection.Trim()));
        }
        if (FtsQuery(filter.Query) is { } match)
        {
            where.Add("v.rowid IN (SELECT rowid FROM canvases_fts WHERE canvases_fts MATCH $match)");
            parameters.Add(("$match", match));
        }
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.Command(null,
            "SELECT v.id, v.title, v.description, v.tags, v.collection, v.pinned, v.archived, v.version, v.deleted_at, v.created_at, v.updated_at, " +
            "(SELECT count(*) FROM cards c WHERE c.canvas_id = v.id), (SELECT count(*) FROM regions r WHERE r.canvas_id = v.id), " +
            "(SELECT count(*) FROM cards c WHERE c.canvas_id = v.id AND c.kind = 'question') " +
            $"FROM canvases v WHERE {string.Join(" AND ", where)} ORDER BY v.pinned DESC, v.updated_at DESC LIMIT $limit", [.. parameters]);
        var results = new List<CanvasSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = Rows.ReadCanvas(reader);
            results.Add(new(row.Meta.Id, row.Meta.Title, row.Meta.Description, row.Meta.Tags, row.Meta.Collection, row.Meta.Pinned, row.Meta.Archived,
                row.Version, reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), row.Meta.CreatedAt, row.UpdatedAt, row.DeletedAt));
        }
        return results;
    }

    public async Task<CanvasRow> Row(string id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        return await LiveRow(connection, id, cancellationToken);
    }

    public async Task<(CanvasRow Row, CanvasState State)> Current(string id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var row = await LiveRow(connection, id, cancellationToken, transaction);
        return (row, await Rows.State(connection, transaction, row.Meta, cancellationToken));
    }

    public async Task<CanvasDocument> Document(string id, CancellationToken cancellationToken)
    {
        var (row, state) = await Current(id, cancellationToken);
        return ToDocument(state, row.Version, row.UpdatedAt);
    }

    public static CanvasDocument ToDocument(CanvasState state, long version, DateTimeOffset updatedAt) => new(
        state.Meta!, version, updatedAt,
        [.. state.Regions.Values.OrderBy(r => r.ParentId is null ? 0 : 1).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id, StringComparer.Ordinal)],
        [.. state.Cards.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal)],
        [.. state.Connections.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal)],
        LayoutEngine.FindOverlaps(state));

    public async Task<(long Version, string Text)> Outline(string id, OutlineOptions options, CancellationToken cancellationToken)
    {
        var (row, state) = await Current(id, cancellationToken);
        return (row.Version, OutlineRenderer.Render(state, row.Version, options));
    }

    public async Task<List<HistoryEntry>> History(string id, long? before, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await LiveRow(connection, id, cancellationToken);
        await using var command = connection.Command(null,
            "SELECT version, kind, actor, client, summary, reverts_to, undoes, op_count, created_at FROM batches WHERE canvas_id = $id AND version < $before " +
            "ORDER BY version DESC LIMIT $limit", ("$id", id), ("$before", before ?? long.MaxValue), ("$limit", Math.Clamp(limit, 1, 200)));
        var entries = new List<HistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) entries.Add(ReadHistory(reader));
        return entries;
    }

    public async Task<BatchDetail> Batch(string id, long version, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await LiveRow(connection, id, cancellationToken);
        var entry = await HistoryEntry(connection, null, id, version, cancellationToken) ?? throw SprawlException.NotFound($"Version {version} does not exist.");
        var ops = await Ops(connection, null, id, version, version, cancellationToken);
        return new(entry, [.. ops.Select(op => new OpRecord(op.Type, op.EntityId, SprawlJson.Element(SprawlJson.Entity(op.Before)), SprawlJson.Element(SprawlJson.Entity(op.After))))]);
    }

    public async Task<CanvasDocument> StateAt(string id, long version, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var row = await LiveRow(connection, id, cancellationToken);
        if (version < 1 || version > row.Version) throw SprawlException.Invalid($"Version must be between 1 and {row.Version}.");
        var (state, at) = await StateAt(connection, null, id, version, cancellationToken);
        return ToDocument(state, version, at);
    }

    /// <summary>The state after <paramref name="version"/>: the nearest snapshot at or before it, plus a replay of later ops.</summary>
    public static async Task<(CanvasState State, DateTimeOffset At)> StateAt(SqliteConnection connection, SqliteTransaction? transaction, string id, long version, CancellationToken cancellationToken)
    {
        var state = new CanvasState();
        long from = 0;
        await using (var command = connection.Command(transaction, "SELECT version, state FROM snapshots WHERE canvas_id = $id AND version <= $v ORDER BY version DESC LIMIT 1", ("$id", id), ("$v", version)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            if (await reader.ReadAsync(cancellationToken))
            {
                from = reader.GetInt64(0);
                state = CanvasState.FromSnapshot(SprawlJson.Decompress((byte[])reader.GetValue(1)));
            }
        foreach (var op in await Ops(connection, transaction, id, from + 1, version, cancellationToken)) StateOps.Apply(state, op);
        var entry = await HistoryEntry(connection, transaction, id, version, cancellationToken);
        return (state, entry?.CreatedAt ?? DateTimeOffset.MinValue);
    }

    public static async Task<List<Op>> Ops(SqliteConnection connection, SqliteTransaction? transaction, string id, long fromVersion, long toVersion, CancellationToken cancellationToken)
    {
        await using var command = connection.Command(transaction,
            "SELECT type, entity_id, before_json, after_json FROM ops WHERE canvas_id = $id AND version >= $from AND version <= $to ORDER BY version, seq",
            ("$id", id), ("$from", fromVersion), ("$to", toVersion));
        var ops = new List<Op>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entityId = reader.GetString(1);
            ops.Add(new(reader.GetString(0), entityId, SprawlJson.ReadEntity(entityId, reader.NullableString(2)), SprawlJson.ReadEntity(entityId, reader.NullableString(3))));
        }
        return ops;
    }

    /// <summary>Committed batches after <paramref name="since"/> as SSE events, or null when more than <paramref name="max"/> are missing.</summary>
    public async Task<List<CanvasEvent>?> EventsSince(string id, long since, int max, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var row = await LiveRow(connection, id, cancellationToken);
        if (row.Version - since > max) return null;
        var events = new List<CanvasEvent>();
        for (var version = since + 1; version <= row.Version; version++)
        {
            var entry = await HistoryEntry(connection, null, id, version, cancellationToken);
            if (entry is null) continue;
            var ops = await Ops(connection, null, id, version, version, cancellationToken);
            events.Add(ToEvent(id, entry, ops));
        }
        return events;
    }

    public static CanvasEvent ToEvent(string canvasId, HistoryEntry entry, IEnumerable<Op> ops) => new(canvasId, entry.Version, entry.Kind, entry.Actor, entry.Client,
        entry.Summary, [.. ops.Select(op => new OpRecord(op.Type, op.EntityId, null, SprawlJson.Element(SprawlJson.Entity(op.After))))], entry.CreatedAt);

    public async Task<List<CardDetail>> Cards(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (ids.Count is 0 or > Limits.CardsPerQuery) throw SprawlException.Invalid($"Ask for 1-{Limits.CardsPerQuery} cards.");
        await using var connection = await database.OpenAsync(cancellationToken);
        var cards = await Rows.CardsById(connection, [.. ids.Distinct(StringComparer.Ordinal)], cancellationToken);
        var details = new List<CardDetail>();
        foreach (var card in ids.Distinct(StringComparer.Ordinal).Select(id => cards.FirstOrDefault(c => c.Id == id)).OfType<Card>())
        {
            await using var command = connection.Command(null,
                "SELECT c.canvas_id, (SELECT title FROM regions WHERE id = $region) FROM cards c WHERE c.id = $id", ("$id", card.Id), ("$region", card.RegionId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var canvasId = reader.GetString(0);
            var regionTitle = reader.NullableString(1);
            details.Add(new(card, canvasId, regionTitle, await ConnectionsOf(connection, card.Id, cancellationToken)));
        }
        return details;
    }

    private static async Task<List<Connection>> ConnectionsOf(SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.Command(null,
            "SELECT id, source_id, target_id, kind, label, created_by, created_at FROM connections WHERE source_id = $id OR target_id = $id ORDER BY created_at, id", ("$id", id));
        var list = new List<Connection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.NullableString(4), reader.GetString(5), reader.Time(6)));
        return list;
    }

    public async Task<SearchResult> Search(string query, string? canvasId, IReadOnlyList<string> kinds, int limit, CancellationToken cancellationToken)
    {
        var match = FtsQuery(query) ?? throw SprawlException.Invalid("Search needs at least one word.");
        limit = Math.Clamp(limit, 1, 50);
        var kindFilter = kinds.Select(kind => Text.OneOf(kind, CardKinds.All, "card kind")).Distinct().ToList();
        await using var connection = await database.OpenAsync(cancellationToken);
        var cardWhere = "cards_fts MATCH $match AND v.deleted_at IS NULL" + (canvasId is null ? "" : " AND c.canvas_id = $canvas") +
            (kindFilter.Count == 0 ? "" : $" AND c.kind IN ({string.Join(", ", kindFilter.Select((_, i) => "$k" + i))})");
        var parameters = new List<(string, object?)> { ("$match", match), ("$limit", limit), ("$canvas", canvasId) };
        parameters.AddRange(kindFilter.Select((kind, i) => ("$k" + i, (object?)kind)));
        var cards = new List<CardSearchHit>();
        await using (var command = connection.Command(null,
            "SELECT c.canvas_id, v.title, c.id, c.region_id, r.title, c.kind, c.title, snippet(cards_fts, 1, '«', '»', '…', 12) " +
            "FROM cards_fts JOIN cards c ON c.rowid = cards_fts.rowid JOIN canvases v ON v.id = c.canvas_id LEFT JOIN regions r ON r.id = c.region_id " +
            $"WHERE {cardWhere} ORDER BY bm25(cards_fts, 8.0, 1.0, 2.0) LIMIT $limit", [.. parameters]))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                cards.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.NullableString(3), reader.NullableString(4),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        var canvases = new List<CanvasSearchHit>();
        if (canvasId is null)
        {
            await using var command = connection.Command(null,
                "SELECT v.id, v.title, snippet(canvases_fts, 1, '«', '»', '…', 12) FROM canvases_fts JOIN canvases v ON v.rowid = canvases_fts.rowid " +
                "WHERE canvases_fts MATCH $match AND v.deleted_at IS NULL ORDER BY bm25(canvases_fts, 8.0, 1.0, 4.0) LIMIT 10", ("$match", match));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) canvases.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return new(canvases, cards);
    }

    /// <summary>Turn free text into a safe FTS5 query: every word must match, each as a prefix. No FTS syntax is passed through.</summary>
    public static string? FtsQuery(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var words = Word().Matches(text).Select(match => match.Value).Where(word => word.Length > 0).Take(8).ToList();
        return words.Count == 0 ? null : string.Join(' ', words.Select(word => $"\"{word}\"*"));
    }

    private static async Task<CanvasRow> LiveRow(SqliteConnection connection, string id, CancellationToken cancellationToken, SqliteTransaction? transaction = null) =>
        await Rows.Canvas(connection, transaction, id, cancellationToken) is { DeletedAt: null } row ? row : throw SprawlException.NotFound($"Canvas '{id}' was not found.");

    public static async Task<HistoryEntry?> HistoryEntry(SqliteConnection connection, SqliteTransaction? transaction, string id, long version, CancellationToken cancellationToken)
    {
        await using var command = connection.Command(transaction,
            "SELECT version, kind, actor, client, summary, reverts_to, undoes, op_count, created_at FROM batches WHERE canvas_id = $id AND version = $v", ("$id", id), ("$v", version));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHistory(reader) : null;
    }

    private static HistoryEntry ReadHistory(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.NullableString(3), r.GetString(4),
        r.IsDBNull(5) ? null : r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6), r.GetInt32(7), r.Time(8));

    [GeneratedRegex(@"[\p{L}\p{N}_]+")] private static partial Regex Word();

    internal static string Serialize(object value) => JsonSerializer.Serialize(value, SprawlJson.Options);
}
