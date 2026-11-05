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
    IReadOnlyList<LayoutWarning> LayoutWarnings,
    IReadOnlyList<Sketch>? Sketches = null);

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

/// <summary>A card named in an analysis: enough to show and jump to it.</summary>
public sealed record CardRef(string Id, string Title, string Kind, string? RegionId, string? RegionTitle);

/// <param name="SharedTerms">The words (and <c>#tags</c>) the two cards have in common, most telling first.</param>
public sealed record RelatedCard(CardRef Card, double Score, IReadOnlyList<string> SharedTerms);

/// <summary>A card on another canvas, reached by a <c>[[Canvas/Card]]</c> link.</summary>
public sealed record ExternalCardRef(string CanvasId, string CanvasTitle, CardRef Card);

/// <summary>A card's place in the canvas's web of meaning, derived from text and connections.</summary>
/// <param name="CrossLinks">Cards on other canvases this card links to.</param>
/// <param name="CrossBacklinks">Cards on other canvases that link to this card.</param>
public sealed record CardContext(
    string CanvasId,
    long Version,
    CardRef Card,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CardRef> Links,
    IReadOnlyList<string> UnresolvedLinks,
    IReadOnlyList<CardRef> Backlinks,
    IReadOnlyList<CardRef> Mentions,
    IReadOnlyList<RelatedCard> Related,
    IReadOnlyList<ExternalCardRef> CrossLinks,
    IReadOnlyList<ExternalCardRef> CrossBacklinks);

/// <summary>Where a <c>[[link]]</c> written on one canvas leads.</summary>
public sealed record LinkTarget(string CanvasId, string CanvasTitle, string CardId, string CardTitle);

/// <summary>Two regions that talk about the same things but have no connection or link between them.</summary>
public sealed record RegionGap(string FirstId, string FirstTitle, string SecondId, string SecondTitle, double Similarity, IReadOnlyList<string> SharedTerms);

/// <summary>A <c>[[target]]</c> that names no card yet, and the cards that use it.</summary>
public sealed record DanglingLink(string Target, IReadOnlyList<CardRef> From);

public sealed record TagCount(string Tag, int Count);

public sealed record Hub(CardRef Card, int Degree);

public sealed record EdgeRef(string ConnectionId, string FromId, string FromTitle, string ToId, string ToTitle, string? Label);

public sealed record CanvasInsights(
    string CanvasId,
    long Version,
    int Cards,
    int Regions,
    int Connections,
    int TextLinks,
    IReadOnlyList<RegionGap> Gaps,
    IReadOnlyList<CardRef> Orphans,
    IReadOnlyList<DanglingLink> DanglingLinks,
    IReadOnlyList<CardRef> OpenQuestions,
    IReadOnlyList<EdgeRef> Contradictions,
    IReadOnlyList<Hub> Hubs,
    IReadOnlyList<TagCount> Tags);

public static class ReviewGrades
{
    /// <summary>Still open and still pressing: back tomorrow.</summary>
    public const string Again = "again";
    public const string Hard = "hard";
    public const string Good = "good";
    /// <summary>Settled in your head, if not yet on the canvas: wait much longer.</summary>
    public const string Easy = "easy";
    public static readonly string[] All = [Again, Hard, Good, Easy];
}

/// <summary>Where one open question is in its review schedule. Review state lives outside the op log: it is not canvas content.</summary>
public sealed record ReviewState(int Reps, double Ease, double IntervalDays, DateTimeOffset DueAt, DateTimeOffset? LastReviewedAt);

public sealed record DueQuestion(string CanvasId, string CanvasTitle, CardRef Card, string Body, ReviewState Review);

public sealed record ReviewQueue(int DueCount, IReadOnlyList<DueQuestion> Due);

public sealed record ReviewRequest(string Grade);

public sealed record ReviewResult(string CardId, ReviewState Review);
