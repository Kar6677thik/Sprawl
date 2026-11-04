using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Data;

public sealed record CanvasRow(CanvasMeta Meta, long Version, DateTimeOffset? DeletedAt, DateTimeOffset UpdatedAt);

/// <summary>Reading and writing current-state rows. Every method takes the caller's connection and transaction.</summary>
public static class Rows
{
    private const string CanvasColumns = "id, title, description, tags, collection, pinned, archived, version, deleted_at, created_at, updated_at";
    private const string RegionColumns = "id, parent_id, title, tint, cols, x, y, w, h, placement, created_by, created_at, updated_at";
    private const string CardColumns = "id, region_id, kind, title, body, url, language, tint, emphasis, size, x, y, w, h, placement, size_source, created_by, updated_by, created_at, updated_at";
    private const string ConnectionColumns = "id, source_id, target_id, kind, label, created_by, created_at";

    public static async Task<CanvasRow?> Canvas(SqliteConnection connection, SqliteTransaction? transaction, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.Command(transaction, $"SELECT {CanvasColumns} FROM canvases WHERE id = $id", ("$id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCanvas(reader) : null;
    }

    public static CanvasRow ReadCanvas(SqliteDataReader reader) => new(
        new CanvasMeta(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [], reader.NullableString(4), reader.GetInt64(5) != 0,
            reader.GetInt64(6) != 0, reader.Time(9)),
        reader.GetInt64(7), reader.NullableTime(8), reader.Time(10));

    public static async Task<CanvasState> State(SqliteConnection connection, SqliteTransaction? transaction, CanvasMeta meta, CancellationToken cancellationToken)
    {
        var state = new CanvasState { Meta = meta };
        await using (var command = connection.Command(transaction, $"SELECT {RegionColumns} FROM regions WHERE canvas_id = $id", ("$id", meta.Id)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var region = ReadRegion(reader);
                state.Regions[region.Id] = region;
            }
        await using (var command = connection.Command(transaction, $"SELECT {CardColumns} FROM cards WHERE canvas_id = $id", ("$id", meta.Id)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var card = ReadCard(reader);
                state.Cards[card.Id] = card;
            }
        await using (var command = connection.Command(transaction, $"SELECT {ConnectionColumns} FROM connections WHERE canvas_id = $id", ("$id", meta.Id)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var edge = ReadConnection(reader);
                state.Connections[edge.Id] = edge;
            }
        return state;
    }

    public static async Task<List<Card>> CardsById(SqliteConnection connection, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var names = ids.Select((_, i) => "$c" + i).ToList();
        await using var command = connection.Command(null,
            $"SELECT {string.Join(", ", CardColumns.Split(", ").Select(c => "c." + c))}, c.canvas_id FROM cards c JOIN canvases v ON v.id = c.canvas_id " +
            $"WHERE v.deleted_at IS NULL AND c.id IN ({string.Join(", ", names)})",
            [.. ids.Select((id, i) => (names[i], (object?)id))]);
        var cards = new List<Card>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) cards.Add(ReadCard(reader));
        return cards;
    }

    private static Region ReadRegion(SqliteDataReader r) => new(r.GetString(0), r.NullableString(1), r.GetString(2), r.NullableString(3), r.GetInt32(4),
        r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.GetString(9), r.GetString(10), r.Time(11), r.Time(12));

    private static Card ReadCard(SqliteDataReader r) => new(r.GetString(0), r.NullableString(1), r.GetString(2), r.GetString(3), r.GetString(4),
        r.NullableString(5), r.NullableString(6), r.NullableString(7), r.GetString(8), r.GetString(9), r.GetInt32(10), r.GetInt32(11), r.GetInt32(12),
        r.GetInt32(13), r.GetString(14), r.GetString(15), r.GetString(16), r.GetString(17), r.Time(18), r.Time(19));

    private static Connection ReadConnection(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.NullableString(4), r.GetString(5), r.Time(6));

    /// <summary>Make the current-state tables match the ops' after-images. One op per entity, so order does not matter.</summary>
    public static async Task Apply(SqliteConnection connection, SqliteTransaction transaction, string canvasId, IEnumerable<Op> ops, CancellationToken cancellationToken)
    {
        foreach (var op in ops)
        {
            switch (op.After)
            {
                case null:
                    var table = EntityIds.KindOf(op.EntityId) switch
                    {
                        EntityKind.Region => "regions",
                        EntityKind.Card => "cards",
                        EntityKind.Connection => "connections",
                        _ => throw new InvalidOperationException("Canvas metadata is never deleted by an op.")
                    };
                    await connection.Exec(transaction, $"DELETE FROM {table} WHERE id = $id AND canvas_id = $canvas", cancellationToken, ("$id", op.EntityId), ("$canvas", canvasId));
                    break;
                case CanvasMeta meta:
                    await connection.Exec(transaction,
                        "UPDATE canvases SET title = $title, description = $description, tags = $tags, collection = $collection, pinned = $pinned, archived = $archived WHERE id = $id",
                        cancellationToken, ("$id", meta.Id), ("$title", meta.Title), ("$description", meta.Description), ("$tags", JsonSerializer.Serialize(meta.Tags)),
                        ("$collection", meta.Collection), ("$pinned", meta.Pinned ? 1 : 0), ("$archived", meta.Archived ? 1 : 0));
                    break;
                case Region r:
                    await connection.Exec(transaction,
                        $"INSERT INTO regions (canvas_id, {RegionColumns}) VALUES ($canvas, $id, $parent, $title, $tint, $cols, $x, $y, $w, $h, $placement, $by, $created, $updated) " +
                        "ON CONFLICT(id) DO UPDATE SET parent_id = excluded.parent_id, title = excluded.title, tint = excluded.tint, cols = excluded.cols, x = excluded.x, y = excluded.y, " +
                        "w = excluded.w, h = excluded.h, placement = excluded.placement, created_by = excluded.created_by, created_at = excluded.created_at, updated_at = excluded.updated_at",
                        cancellationToken, ("$canvas", canvasId), ("$id", r.Id), ("$parent", r.ParentId), ("$title", r.Title), ("$tint", r.Tint), ("$cols", r.Cols),
                        ("$x", r.X), ("$y", r.Y), ("$w", r.W), ("$h", r.H), ("$placement", r.Placement), ("$by", r.CreatedBy), ("$created", Sql.Iso(r.CreatedAt)), ("$updated", Sql.Iso(r.UpdatedAt)));
                    break;
                case Card c:
                    await connection.Exec(transaction,
                        $"INSERT INTO cards (canvas_id, {CardColumns}) VALUES ($canvas, $id, $region, $kind, $title, $body, $url, $language, $tint, $emphasis, $size, $x, $y, $w, $h, " +
                        "$placement, $sizeSource, $createdBy, $updatedBy, $created, $updated) ON CONFLICT(id) DO UPDATE SET region_id = excluded.region_id, kind = excluded.kind, " +
                        "title = excluded.title, body = excluded.body, url = excluded.url, language = excluded.language, tint = excluded.tint, emphasis = excluded.emphasis, " +
                        "size = excluded.size, x = excluded.x, y = excluded.y, w = excluded.w, h = excluded.h, placement = excluded.placement, size_source = excluded.size_source, " +
                        "created_by = excluded.created_by, updated_by = excluded.updated_by, created_at = excluded.created_at, updated_at = excluded.updated_at",
                        cancellationToken, ("$canvas", canvasId), ("$id", c.Id), ("$region", c.RegionId), ("$kind", c.Kind), ("$title", c.Title), ("$body", c.Body), ("$url", c.Url),
                        ("$language", c.Language), ("$tint", c.Tint), ("$emphasis", c.Emphasis), ("$size", c.Size), ("$x", c.X), ("$y", c.Y), ("$w", c.W), ("$h", c.H),
                        ("$placement", c.Placement), ("$sizeSource", c.SizeSource), ("$createdBy", c.CreatedBy), ("$updatedBy", c.UpdatedBy),
                        ("$created", Sql.Iso(c.CreatedAt)), ("$updated", Sql.Iso(c.UpdatedAt)));
                    break;
                case Connection e:
                    await connection.Exec(transaction,
                        $"INSERT INTO connections (canvas_id, {ConnectionColumns}) VALUES ($canvas, $id, $source, $target, $kind, $label, $by, $created) " +
                        "ON CONFLICT(id) DO UPDATE SET source_id = excluded.source_id, target_id = excluded.target_id, kind = excluded.kind, label = excluded.label, " +
                        "created_by = excluded.created_by, created_at = excluded.created_at",
                        cancellationToken, ("$canvas", canvasId), ("$id", e.Id), ("$source", e.SourceId), ("$target", e.TargetId), ("$kind", e.Kind), ("$label", e.Label),
                        ("$by", e.CreatedBy), ("$created", Sql.Iso(e.CreatedAt)));
                    break;
            }
        }
    }
}
