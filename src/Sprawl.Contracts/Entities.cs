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
    DateTimeOffset UpdatedAt) : IEntity;

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
    DateTimeOffset UpdatedAt) : IEntity;

/// <summary>An edge between two cards, a card and a region, or two regions. The endpoint type is in the ID prefix.</summary>
public sealed record Connection(
    string Id,
    string SourceId,
    string TargetId,
    string Kind,
    string? Label,
    string CreatedBy,
    DateTimeOffset CreatedAt) : IEntity;
