using System.Text.Json.Serialization;

namespace Sprawl.Contracts;

/// <summary>
/// A semantic edit. The UI and the MCP send the same intents; the server resolves them into positioned operations.
/// References to regions accept a batch <c>ref</c>, an ID, or a title (case-insensitive). References to cards accept a
/// batch <c>ref</c> or an ID.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(AddRegionIntent), "addRegion")]
[JsonDerivedType(typeof(AddCardIntent), "addCard")]
[JsonDerivedType(typeof(UpdateCardIntent), "updateCard")]
[JsonDerivedType(typeof(ConnectIntent), "connect")]
[JsonDerivedType(typeof(DisconnectIntent), "disconnect")]
[JsonDerivedType(typeof(MoveCardsIntent), "moveCards")]
[JsonDerivedType(typeof(UpdateRegionIntent), "updateRegion")]
[JsonDerivedType(typeof(NestRegionIntent), "nestRegion")]
[JsonDerivedType(typeof(MergeRegionsIntent), "mergeRegions")]
[JsonDerivedType(typeof(MergeCardsIntent), "mergeCards")]
[JsonDerivedType(typeof(DeleteRegionIntent), "deleteRegion")]
[JsonDerivedType(typeof(DeleteCardsIntent), "deleteCards")]
[JsonDerivedType(typeof(UpdateCanvasIntent), "updateCanvas")]
[JsonDerivedType(typeof(SetLayoutIntent), "setLayout")]
[JsonDerivedType(typeof(UnpinIntent), "unpin")]
[JsonDerivedType(typeof(AddSketchIntent), "addSketch")]
[JsonDerivedType(typeof(DeleteSketchesIntent), "deleteSketches")]
[JsonDerivedType(typeof(UpdateConnectionIntent), "updateConnection")]
[JsonDerivedType(typeof(AddDiagramIntent), "addDiagram")]
[JsonDerivedType(typeof(AddWalkthroughIntent), "addWalkthrough")]
[JsonDerivedType(typeof(DeleteWalkthroughIntent), "deleteWalkthrough")]
public abstract record Intent;

/// <param name="Near">A region (ID or title) to place the new region next to.</param>
public sealed record AddRegionIntent(string Title, string? Ref = null, string? Parent = null, string? Tint = null, string? Near = null, string? Layout = null) : Intent;

/// <param name="At">Owner only: an explicit position, which makes the card manual. Relative to the region, or canvas-absolute when no region is given.</param>
public sealed record AddCardIntent(
    string Kind,
    string Title,
    string? Region = null,
    string? Ref = null,
    string? Body = null,
    string? Url = null,
    string? Language = null,
    string? Emphasis = null,
    string? Tint = null,
    Point? At = null,
    string? Shape = null) : Intent;

public sealed record UpdateCardIntent(
    string Id,
    string? Title = null,
    string? Body = null,
    string? AppendBody = null,
    string? Kind = null,
    string? Url = null,
    string? Language = null,
    string? Emphasis = null,
    string? Tint = null,
    string? Shape = null) : Intent;

public sealed record ConnectIntent(string From, string To, string? Kind = null, string? Label = null, string? Line = null, string? Ends = null) : Intent;

/// <summary>Change a connection in place. A label of "" clears it.</summary>
public sealed record UpdateConnectionIntent(string Id, string? Kind = null, string? Label = null, string? Line = null, string? Ends = null) : Intent;

/// <summary>
/// A diagram from a Mermaid flowchart (<c>flowchart TD</c> / <c>LR</c>): a new diagram region holding a shape per node,
/// a connection per edge and a sub-region per subgraph, laid out by the server. Refs <c>{Ref}:{nodeId}</c> name the nodes.
/// </summary>
/// <param name="Replace">When a region with this title exists, clear its shapes, groups and their connections first, so the diagram is redrawn in place.</param>
public sealed record AddDiagramIntent(string Title, string Mermaid, string? Ref = null, string? Parent = null, string? Near = null, string? Layout = null, bool Replace = false) : Intent;

/// <summary>Remove by connection <paramref name="Id"/>, or by endpoints (and optionally kind).</summary>
public sealed record DisconnectIntent(string? Id = null, string? From = null, string? To = null, string? Kind = null) : Intent;

public sealed record MoveCardsIntent(IReadOnlyList<string> Ids, string ToRegion, bool CreateRegion = false) : Intent;

public sealed record UpdateRegionIntent(string Id, string? Title = null, string? Tint = null, string? Layout = null) : Intent;

/// <param name="Parent">The new parent region, or null to make it top-level.</param>
public sealed record NestRegionIntent(string Id, string? Parent) : Intent;

public sealed record MergeRegionsIntent(IReadOnlyList<string> SourceIds, string IntoId) : Intent;

/// <summary>Keep <paramref name="Into"/>, fold the other cards into it, rewire their connections and delete them.</summary>
public sealed record MergeCardsIntent(IReadOnlyList<string> Ids, string Into, string? Title = null, string? Body = null) : Intent;

/// <summary>Delete an empty region.</summary>
public sealed record DeleteRegionIntent(string Id) : Intent;

public sealed record DeleteCardsIntent(IReadOnlyList<string> Ids) : Intent;

public sealed record UpdateCanvasIntent(
    string? Title = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? AddTags = null,
    IReadOnlyList<string>? RemoveTags = null,
    string? Collection = null,
    bool ClearCollection = false,
    bool? Pinned = null,
    bool? Archived = null) : Intent;

/// <summary>Owner only. Explicit positions from a drag, resize or Tidy.</summary>
public sealed record SetLayoutIntent(IReadOnlyList<LayoutItem> Items) : Intent;

/// <param name="Container">Move into a region (ID), or "canvas" to make a card loose. Null keeps the current container.</param>
/// <param name="AsAuto">Tidy output: keep the item automatically placed instead of pinning it.</param>
public sealed record LayoutItem(string Id, double X, double Y, double? W = null, double? H = null, string? Container = null, bool AsAuto = false);

/// <summary>Owner only. Hand items back to automatic placement.</summary>
public sealed record UnpinIntent(IReadOnlyList<string> Ids) : Intent;

/// <summary>Owner only: a freehand stroke. X/Y place its box: relative to <paramref name="Region"/>, or canvas-absolute when none.</summary>
/// <param name="Path">SVG path data in the box's own coordinates (0..W, 0..H).</param>
public sealed record AddSketchIntent(string Path, double X, double Y, int W, int H, string? Region = null, string? Tint = null, int? Stroke = null, string? Ref = null) : Intent;

/// <summary>
/// A walkthrough of a region. Each step's highlight names cards, sub-regions or connections by ref, id or title (titles are
/// looked up inside the region). With <paramref name="Replace"/>, a walkthrough of the same title in the region is rewritten.
/// </summary>
public sealed record AddWalkthroughIntent(string Region, string Title, IReadOnlyList<WalkthroughStep> Steps, string? Ref = null, bool Replace = false) : Intent;

public sealed record DeleteWalkthroughIntent(string Id) : Intent;

/// <summary>Owner only.</summary>
public sealed record DeleteSketchesIntent(IReadOnlyList<string> Ids) : Intent;

public sealed record Point(double X, double Y);
