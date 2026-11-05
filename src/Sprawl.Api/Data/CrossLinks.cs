using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Data;

/// <summary>
/// <c>[[links]]</c> between canvases. A target that names no card on its own canvas is tried as <c>Canvas/Card</c>
/// (canvas by id or title, card by id or title) or as a bare card id, which is global. Only live canvases count.
/// Nothing is stored: links are read from text, so they follow renames and edits like local links do.
/// </summary>
public sealed class CrossLinks(Database database, CanvasStore store)
{
    private sealed record CanvasInfo(string Id, string Title);

    /// <summary>Where a link written on <paramref name="fromCanvasId"/> leads, or null when it leads nowhere.</summary>
    public async Task<LinkTarget?> Resolve(string fromCanvasId, string target, CancellationToken cancellationToken)
    {
        var (_, local) = await store.Current(fromCanvasId, cancellationToken);
        if (Semantics.LinkGraph.Build(local).Resolve(target) is { } here)
            return new(fromCanvasId, local.Meta!.Title, here.Id, here.Title);
        return await External(fromCanvasId, target, new(StringComparer.Ordinal), cancellationToken) is { } found
            ? new(found.CanvasId, found.CanvasTitle, found.Card.Id, found.Card.Title) : null;
    }

    /// <summary>The card context plus links to and from other canvases; external links are dropped from Unresolved.</summary>
    public async Task<CardContext> Extend(CardContext context, Card card, CanvasState state, CancellationToken cancellationToken)
    {
        var cache = new Dictionary<string, CanvasState?>(StringComparer.Ordinal);
        var outgoing = new List<ExternalCardRef>();
        var unresolved = new List<string>();
        foreach (var target in context.UnresolvedLinks)
        {
            var found = await External(context.CanvasId, target, cache, cancellationToken);
            if (found is null) unresolved.Add(target);
            else if (outgoing.All(o => o.Card.Id != found.Card.Id)) outgoing.Add(found);
        }
        return context with { UnresolvedLinks = unresolved, CrossLinks = outgoing, CrossBacklinks = await Backlinks(context.CanvasId, state.Meta!.Title, card, cancellationToken) };
    }

    /// <summary>Of these targets (written on <paramref name="canvasId"/>), the ones that lead to another canvas.</summary>
    public async Task<HashSet<string>> Reachable(string canvasId, IEnumerable<string> targets, CancellationToken cancellationToken)
    {
        var cache = new Dictionary<string, CanvasState?>(StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
            if (await External(canvasId, target, cache, cancellationToken) is not null) reachable.Add(target);
        return reachable;
    }

    private async Task<ExternalCardRef?> External(string fromCanvasId, string target, Dictionary<string, CanvasState?> cache, CancellationToken cancellationToken)
    {
        target = target.Trim();
        if (EntityIds.KindOf(target) == EntityKind.Card && EntityIds.IsWellFormed(target))
        {
            var home = await CanvasOfCard(target, cancellationToken);
            if (home is null || home.Id == fromCanvasId) return null;
            var homeState = await StateOf(home.Id, cache, cancellationToken);
            return homeState?.Cards.GetValueOrDefault(target) is { } byId ? new(home.Id, home.Title, Ref(homeState, byId)) : null;
        }
        if (Semantics.SplitCrossTarget(target) is not { } split) return null;
        var canvas = await FindCanvas(split.Canvas, cancellationToken);
        if (canvas is null || canvas.Id == fromCanvasId) return null;
        var state = await StateOf(canvas.Id, cache, cancellationToken);
        return state is not null && Semantics.LinkGraph.Build(state).Resolve(split.Card) is { } card ? new(canvas.Id, canvas.Title, Ref(state, card)) : null;
    }

    /// <summary>Cards on other canvases whose text links here: candidates found by a text match, then confirmed by resolving.</summary>
    private async Task<List<ExternalCardRef>> Backlinks(string canvasId, string canvasTitle, Card card, CancellationToken cancellationToken)
    {
        var candidates = new List<(string CanvasId, string CanvasTitle, string CardId, string Title, string Kind, string? RegionId, string? RegionTitle, string Body)>();
        await using (var connection = await database.OpenAsync(cancellationToken))
        {
            await using var command = connection.Command(null,
                "SELECT c.canvas_id, v.title, c.id, c.title, c.kind, c.region_id, (SELECT title FROM regions WHERE id = c.region_id), c.body " +
                "FROM cards c JOIN canvases v ON v.id = c.canvas_id WHERE v.deleted_at IS NULL AND c.canvas_id <> $canvas AND instr(c.body, '[[') > 0 " +
                "AND (instr(c.body, $id) > 0 OR instr(lower(c.body), lower($title)) > 0) LIMIT 500",
                ("$canvas", canvasId), ("$id", card.Id), ("$title", card.Title));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                candidates.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.NullableString(5), reader.NullableString(6), reader.GetString(7)));
        }
        var result = new List<ExternalCardRef>();
        foreach (var c in candidates)
        {
            var hit = Semantics.LinkTargets(c.Body).Any(target =>
            {
                if (target.Trim() == card.Id) return true;
                if (Semantics.SplitCrossTarget(target) is not { } split) return false;
                var sameCanvas = split.Canvas == canvasId || Text.SameTitle(split.Canvas, canvasTitle);
                return sameCanvas && (split.Card == card.Id || Text.SameTitle(split.Card, card.Title));
            });
            if (hit) result.Add(new(c.CanvasId, c.CanvasTitle, new CardRef(c.CardId, c.Title, c.Kind, c.RegionId, c.RegionTitle)));
        }
        return [.. result.OrderBy(r => r.CanvasTitle, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Card.Title, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<CanvasInfo?> CanvasOfCard(string cardId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.Command(null,
            "SELECT v.id, v.title FROM cards c JOIN canvases v ON v.id = c.canvas_id WHERE c.id = $id AND v.deleted_at IS NULL", ("$id", cardId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetString(0), reader.GetString(1)) : null;
    }

    /// <summary>A live canvas by id, or by title (case- and punctuation-insensitive; the most recently changed wins).</summary>
    private async Task<CanvasInfo?> FindCanvas(string reference, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.Command(null, "SELECT id, title FROM canvases WHERE deleted_at IS NULL ORDER BY updated_at DESC");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        CanvasInfo? byTitle = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            var (id, title) = (reader.GetString(0), reader.GetString(1));
            if (id == reference) return new(id, title);
            if (byTitle is null && Text.SameTitle(title, reference)) byTitle = new(id, title);
        }
        return byTitle;
    }

    private async Task<CanvasState?> StateOf(string canvasId, Dictionary<string, CanvasState?> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(canvasId, out var cached)) return cached;
        CanvasState? state;
        try { state = (await store.Current(canvasId, cancellationToken)).State; }
        catch (SprawlException error) when (error.Status == 404) { state = null; }
        return cache[canvasId] = state;
    }

    private static CardRef Ref(CanvasState state, Card card) => new(card.Id, card.Title, card.Kind, card.RegionId,
        card.RegionId is { } r && state.Regions.TryGetValue(r, out var region) ? region.Title : null);
}
