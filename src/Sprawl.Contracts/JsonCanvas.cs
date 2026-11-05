using System.Text.Json.Serialization;

namespace Sprawl.Contracts;

/// <summary>
/// JSON Canvas 1.0 (https://jsoncanvas.org/spec/1.0/), the open format Obsidian Canvas uses. Unknown properties are
/// ignored on import; optional ones are omitted on export. <c>sprawlKind</c> is Sprawl's own extension (the card kind,
/// which text alone cannot carry); other readers ignore it.
/// </summary>
public sealed record JsonCanvasDocument(IReadOnlyList<JsonCanvasNode>? Nodes = null, IReadOnlyList<JsonCanvasEdge>? Edges = null);

/// <param name="Type">text, file, link or group.</param>
public sealed record JsonCanvasNode(
    string Id,
    string Type,
    double X,
    double Y,
    double Width,
    double Height,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? File = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null,
    [property: JsonPropertyName("sprawlKind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SprawlKind = null);

public sealed record JsonCanvasEdge(
    string Id,
    string FromNode,
    string ToNode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FromSide = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToSide = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FromEnd = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToEnd = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null);

/// <summary>Import a JSON Canvas as a new Sprawl canvas (owner only).</summary>
public sealed record ImportCanvasRequest(string Title, JsonCanvasDocument Canvas, string? Description = null, string? RequestId = null);
