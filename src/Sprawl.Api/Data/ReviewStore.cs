using Microsoft.Data.Sqlite;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Data;

/// <summary>Open questions due for review across live, unarchived canvases, and grading them.</summary>
public sealed class ReviewStore(Database database, CanvasStore canvases, TimeProvider clock)
{
    public async Task<ReviewQueue> Due(string? canvasId, int limit, CancellationToken cancellationToken)
    {
        if (canvasId is not null) await canvases.Row(canvasId, cancellationToken);
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.Command(null,
            "SELECT c.id, c.canvas_id, v.title, c.title, c.region_id, (SELECT title FROM regions WHERE id = c.region_id), c.body, c.created_at, " +
            "r.reps, r.ease, r.interval_days, r.due_at, r.last_reviewed_at " +
            "FROM cards c JOIN canvases v ON v.id = c.canvas_id LEFT JOIN reviews r ON r.card_id = c.id " +
            "WHERE c.kind = 'question' AND v.deleted_at IS NULL AND v.archived = 0 AND ($canvas IS NULL OR c.canvas_id = $canvas)", ("$canvas", canvasId));
        var now = clock.GetUtcNow();
        var due = new List<DueQuestion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var review = State(reader, 8, reader.Time(7));
            if (review.DueAt > now) continue;
            var card = new CardRef(reader.GetString(0), reader.GetString(3), CardKinds.Question, reader.NullableString(4), reader.NullableString(5));
            due.Add(new(reader.GetString(1), reader.GetString(2), card, reader.GetString(6), review));
        }
        var ordered = due.OrderBy(d => d.Review.DueAt).ThenBy(d => d.Card.Id, StringComparer.Ordinal).ToList();
        return new(ordered.Count, [.. ordered.Take(Math.Clamp(limit, 1, 100))]);
    }

    public async Task<ReviewResult> Grade(string cardId, string grade, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        string canvasId;
        ReviewState current;
        await using (var command = connection.Command(transaction,
            "SELECT c.canvas_id, c.kind, c.created_at, r.reps, r.ease, r.interval_days, r.due_at, r.last_reviewed_at " +
            "FROM cards c JOIN canvases v ON v.id = c.canvas_id AND v.deleted_at IS NULL LEFT JOIN reviews r ON r.card_id = c.id WHERE c.id = $id", ("$id", cardId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) throw SprawlException.NotFound($"Card {cardId} does not exist.");
            if (reader.GetString(1) != CardKinds.Question) throw SprawlException.Invalid("Only question cards are reviewed.");
            canvasId = reader.GetString(0);
            current = State(reader, 3, reader.Time(2));
        }
        var next = Review.Next(current, (grade ?? "").Trim().ToLowerInvariant(), clock.GetUtcNow());
        await connection.Exec(transaction,
            "INSERT INTO reviews (card_id, canvas_id, reps, ease, interval_days, due_at, last_reviewed_at) VALUES ($id, $canvas, $reps, $ease, $interval, $due, $last) " +
            "ON CONFLICT(card_id) DO UPDATE SET reps = $reps, ease = $ease, interval_days = $interval, due_at = $due, last_reviewed_at = $last", cancellationToken,
            ("$id", cardId), ("$canvas", canvasId), ("$reps", next.Reps), ("$ease", next.Ease), ("$interval", next.IntervalDays),
            ("$due", Sql.Iso(next.DueAt)), ("$last", Sql.Iso(next.LastReviewedAt!.Value)));
        await transaction.CommitAsync(cancellationToken);
        return new(cardId, next);
    }

    /// <summary>The stored schedule from column <paramref name="first"/> on, or the initial one when the question was never graded.</summary>
    private static ReviewState State(SqliteDataReader reader, int first, DateTimeOffset createdAt) => reader.IsDBNull(first)
        ? Review.Initial(createdAt)
        : new(reader.GetInt32(first), reader.GetDouble(first + 1), reader.GetDouble(first + 2), reader.Time(first + 3), reader.NullableTime(first + 4));
}
