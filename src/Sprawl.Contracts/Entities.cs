namespace Sprawl.Contracts;

/// <summary>Any canvas entity that can appear in an operation's before/after image.</summary>
public interface IEntity
{
    string Id { get; }
}

/// <summary>
/// Canvas metadata as versioned by the op log. Trash state and the version counter are deliberately not part of it:
/// reverting history never un-deletes or re-deletes a canvas.
/// </summary>
public sealed record CanvasMeta(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    string? Collection,
    bool Pinned,
    bool Archived,
    DateTimeOffset CreatedAt) : IEntity
{
    public bool Equals(CanvasMeta? other) =>
        other is not null && Id == other.Id && Title == other.Title && Description == other.Description &&
        Tags.SequenceEqual(other.Tags, StringComparer.Ordinal) && Collection == other.Collection &&
        Pinned == other.Pinned && Archived == other.Archived && CreatedAt == other.CreatedAt;

    public override int GetHashCode() => HashCode.Combine(Id, Title, Description, Tags.Count, Collection, Pinned, Archived, CreatedAt);
}

/// <summary>A region. Top-level coordinates are canvas-absolute; a sub-region's are relative to its parent.</summary>
/// <param name="Layout">Null or <c>cards</c>: masonry; <c>flow-down</c> / <c>flow-right</c>: a diagram the server lays out as a layered flow.</param>
public sealed record Region(
    string Id,
    string? ParentId,
    string Title,
    string? Tint,
    int Cols,
    int X,
    int Y,
    int W,
    int H,
    string Placement,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Layout = null) : IEntity;

/// <summary>A card. Coordinates are relative to its region, or canvas-absolute for a loose card (always manual).</summary>
public sealed record Card(
    string Id,
    string? RegionId,
    string Kind,
    string Title,
    string Body,
    string? Url,
    string? Language,
    string? Tint,
    string Emphasis,
    string Size,
    int X,
    int Y,
    int W,
    int H,
    string Placement,
    string SizeSource,
    string CreatedBy,
    string UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Shape = null,
    string? Detail = null) : IEntity;

/// <summary>
/// A freehand stroke drawn by the owner: an SVG path (M/L/Q/C/Z commands) in its own box, which is relative to its region
/// (and moves with it) or canvas-absolute when loose. Sketches carry no meaning Claude can read; the outline only counts them.
/// </summary>
public sealed record Sketch(
    string Id,
    string? RegionId,
    string Path,
    string? Tint,
    int Stroke,
    int X,
    int Y,
    int W,
    int H,
    string CreatedBy,
    DateTimeOffset CreatedAt) : IEntity;

/// <summary>A scenario mark on one item for one step: its state (<see cref="MarkStates"/>) and an optional short note shown beside it.</summary>
public sealed record StepMark(string Target, string State, string? Note = null);

/// <summary>
/// One step of a walkthrough: what to say, which cards, regions or connections to light up while saying it, and which
/// of them to mark (down, slow, hot, added, removed, changed): a scenario told step by step over the same diagram.
/// </summary>
public sealed record WalkthroughStep(string Text, IReadOnlyList<string>? Highlight = null, IReadOnlyList<StepMark>? Marks = null)
{
    public bool Equals(WalkthroughStep? other) =>
        other is not null && Text == other.Text && (Highlight ?? []).SequenceEqual(other.Highlight ?? [], StringComparer.Ordinal)
        && (Marks ?? []).SequenceEqual(other.Marks ?? []);

    public override int GetHashCode() => HashCode.Combine(Text, Highlight?.Count ?? 0);
}

/// <summary>
/// A guided tour of a region: ordered steps of explanation, each highlighting what it talks about while the rest fades.
/// How a diagram becomes an explanation (Ilograph's walkthroughs, IcePanel's flows, Structurizr's dynamic views).
/// </summary>
public sealed record Walkthrough(
    string Id,
    string RegionId,
    string Title,
    IReadOnlyList<WalkthroughStep> Steps,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt) : IEntity
{
    public bool Equals(Walkthrough? other) =>
        other is not null && Id == other.Id && RegionId == other.RegionId && Title == other.Title && Steps.SequenceEqual(other.Steps)
        && CreatedBy == other.CreatedBy && CreatedAt == other.CreatedAt && UpdatedAt == other.UpdatedAt;

    public override int GetHashCode() => HashCode.Combine(Id, RegionId, Title, Steps.Count);
}

/// <summary>An edge between two cards, a card and a region, or two regions. The endpoint type is in the ID prefix.</summary>
public sealed record Connection(
    string Id,
    string SourceId,
    string TargetId,
    string Kind,
    string? Label,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? Line = null,
    string? Ends = null) : IEntity;
