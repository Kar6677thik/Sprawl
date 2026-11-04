using System.Text.Json;

namespace Sprawl.Contracts;

/// <param name="OnDuplicate">What to do with a new card whose normalized title already exists in the same region.</param>
public sealed record BatchRequest(
    IReadOnlyList<Intent> Intents,
    string? RequestId = null,
    string? Kind = null,
    bool SkipPinned = false,
    bool CreateMissingRegions = true,
    string OnDuplicate = DuplicatePolicies.Skip);

public sealed record SkippedItem(string Reason, string? Ref = null, string? Id = null, string? ExistingId = null);

public sealed record BatchResult(
    string CanvasId,
    long Version,
    bool Changed,
    string Summary,
    IReadOnlyDictionary<string, string> Refs,
    IReadOnlyList<SkippedItem> Skipped,
    IReadOnlyList<string> Warnings,
    bool Replayed = false);

public sealed record CreateCanvasRequest(
    string Title,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    string? Collection = null,
    IReadOnlyList<Intent>? Intents = null,
    string? RequestId = null,
    string OnDuplicate = DuplicatePolicies.Skip);

public sealed record RevertRequest(long ToVersion);

public sealed record CardsQuery(IReadOnlyList<string> Ids);

public sealed record CanvasSummary(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    string? Collection,
    bool Pinned,
    bool Archived,
    long Version,
    int CardCount,
    int RegionCount,
    int OpenQuestions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt = null);

public sealed record LayoutWarning(string Code, string FirstId, string SecondId);

public sealed record CanvasDocument(
    CanvasMeta Meta,
    long Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<Region> Regions,
    IReadOnlyList<Card> Cards,
    IReadOnlyList<Connection> Connections,
    IReadOnlyList<LayoutWarning> LayoutWarnings);

public sealed record HistoryEntry(
    long Version,
    string Kind,
    string Actor,
    string? Client,
    string Summary,
    long? RevertsTo,
    long? Undoes,
    int OpCount,
    DateTimeOffset CreatedAt);

public sealed record OpRecord(string Type, string EntityId, JsonElement? Before, JsonElement? After);

public sealed record BatchDetail(HistoryEntry Batch, IReadOnlyList<OpRecord> Ops);

public sealed record CardDetail(Card Card, string CanvasId, string? RegionTitle, IReadOnlyList<Connection> Connections);

public sealed record CardSearchHit(string CanvasId, string CanvasTitle, string CardId, string? RegionId, string? RegionTitle, string Kind, string Title, string Snippet);

public sealed record CanvasSearchHit(string CanvasId, string Title, string Snippet);

public sealed record SearchResult(IReadOnlyList<CanvasSearchHit> Canvases, IReadOnlyList<CardSearchHit> Cards);

/// <summary>One SSE <c>batch</c> event: the committed version and its resolved operations (after-images only).</summary>
public sealed record CanvasEvent(
    string CanvasId,
    long Version,
    string Kind,
    string Actor,
    string? Client,
    string Summary,
    IReadOnlyList<OpRecord> Ops,
    DateTimeOffset CreatedAt);
