using System.ComponentModel;
using System.Text.Json.Serialization;
using Sprawl.Contracts;

namespace Sprawl.Mcp.Tools;

[JsonConverter(typeof(JsonStringEnumConverter<CardKind>))]
public enum CardKind
{
    [JsonStringEnumMemberName("idea")] Idea,
    [JsonStringEnumMemberName("note")] Note,
    [JsonStringEnumMemberName("question")] Question,
    [JsonStringEnumMemberName("link")] Link,
    [JsonStringEnumMemberName("code")] Code,
    [JsonStringEnumMemberName("mermaid")] Mermaid,
    [JsonStringEnumMemberName("table")] Table,
    [JsonStringEnumMemberName("chart")] Chart,
    [JsonStringEnumMemberName("widget")] Widget,
}

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionKind>))]
public enum ConnectionKind
{
    [JsonStringEnumMemberName("relates")] Relates,
    [JsonStringEnumMemberName("leads-to")] LeadsTo,
    [JsonStringEnumMemberName("contradicts")] Contradicts,
    [JsonStringEnumMemberName("example-of")] ExampleOf,
    [JsonStringEnumMemberName("depends-on")] DependsOn,
}

[JsonConverter(typeof(JsonStringEnumConverter<Tint>))]
public enum Tint
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("grey")] Grey,
    [JsonStringEnumMemberName("blue")] Blue,
    [JsonStringEnumMemberName("green")] Green,
    [JsonStringEnumMemberName("amber")] Amber,
    [JsonStringEnumMemberName("red")] Red,
    [JsonStringEnumMemberName("violet")] Violet,
}

[JsonConverter(typeof(JsonStringEnumConverter<Shape>))]
public enum Shape
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("rectangle")] Rectangle,
    [JsonStringEnumMemberName("rounded")] Rounded,
    [JsonStringEnumMemberName("terminal")] Terminal,
    [JsonStringEnumMemberName("diamond")] Diamond,
    [JsonStringEnumMemberName("ellipse")] Ellipse,
    [JsonStringEnumMemberName("circle")] Circle,
    [JsonStringEnumMemberName("cylinder")] Cylinder,
    [JsonStringEnumMemberName("parallelogram")] Parallelogram,
    [JsonStringEnumMemberName("hexagon")] Hexagon,
    [JsonStringEnumMemberName("document")] Document,
    [JsonStringEnumMemberName("cloud")] Cloud,
    [JsonStringEnumMemberName("person")] Person,
    [JsonStringEnumMemberName("queue")] Queue,
    [JsonStringEnumMemberName("subroutine")] Subroutine,
    [JsonStringEnumMemberName("text")] Text,
}

[JsonConverter(typeof(JsonStringEnumConverter<RegionLayout>))]
public enum RegionLayout
{
    [JsonStringEnumMemberName("cards")] Cards,
    [JsonStringEnumMemberName("flow-down")] FlowDown,
    [JsonStringEnumMemberName("flow-right")] FlowRight,
}

[JsonConverter(typeof(JsonStringEnumConverter<LineStyle>))]
public enum LineStyle
{
    [JsonStringEnumMemberName("solid")] Solid,
    [JsonStringEnumMemberName("dashed")] Dashed,
    [JsonStringEnumMemberName("dotted")] Dotted,
    [JsonStringEnumMemberName("thick")] Thick,
}

[JsonConverter(typeof(JsonStringEnumConverter<Arrowheads>))]
public enum Arrowheads
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("end")] End,
    [JsonStringEnumMemberName("start")] Start,
    [JsonStringEnumMemberName("both")] Both,
}

[JsonConverter(typeof(JsonStringEnumConverter<Emphasis>))]
public enum Emphasis
{
    [JsonStringEnumMemberName("normal")] Normal,
    [JsonStringEnumMemberName("key")] Key,
}

[JsonConverter(typeof(JsonStringEnumConverter<OutlineDepth>))]
public enum OutlineDepth
{
    [JsonStringEnumMemberName("regions")] Regions,
    [JsonStringEnumMemberName("titles")] Titles,
    [JsonStringEnumMemberName("short")] Short,
    [JsonStringEnumMemberName("full")] Full,
}

[JsonConverter(typeof(JsonStringEnumConverter<DuplicatePolicy>))]
public enum DuplicatePolicy
{
    [JsonStringEnumMemberName("skip")] Skip,
    [JsonStringEnumMemberName("add")] Add,
    [JsonStringEnumMemberName("fail")] Fail,
}

[JsonConverter(typeof(JsonStringEnumConverter<ReorganizeType>))]
public enum ReorganizeType
{
    [JsonStringEnumMemberName("moveCards")] MoveCards,
    [JsonStringEnumMemberName("createRegion")] CreateRegion,
    [JsonStringEnumMemberName("renameRegion")] RenameRegion,
    [JsonStringEnumMemberName("nestRegion")] NestRegion,
    [JsonStringEnumMemberName("mergeRegions")] MergeRegions,
    [JsonStringEnumMemberName("mergeCards")] MergeCards,
    [JsonStringEnumMemberName("deleteEmptyRegion")] DeleteEmptyRegion,
    [JsonStringEnumMemberName("setLayout")] SetLayout,
    [JsonStringEnumMemberName("deleteWalkthrough")] DeleteWalkthrough,
}

public sealed record RegionInput(
    [property: Description("Region title, 1-80 characters. Reusing an existing title (case-insensitive) reuses that region.")] string Title,
    [property: Description("Optional temporary id for this call, so cards in the same call can target this region (e.g. \"r1\").")] string? Ref = null,
    [property: Description("Make it a sub-region of this region (ref, id or title). Regions nest one level only.")] string? Parent = null,
    [property: Description("Optional quiet tint.")] Tint? Tint = null,
    [property: Description("cards (default): masonry columns of cards. flow-down / flow-right: a diagram; the server lays its shapes and connections out as a layered flow (sub-regions become groups).")] RegionLayout? Layout = null);

public sealed record CardInput(
    [property: Description("Region for the card: a ref from this call, a region id, or a region title. A new title creates the region.")] string Region,
    [property: Description("idea: a claim or insight; note: explanation; question: an open question (the canvas's frontier); link: a source (url required); code: a snippet; mermaid: a rendered Mermaid diagram of any type (the body is the source; add_diagram is usually simpler); table: a markdown table (comparisons, trade-offs, cheat sheets), shown as wide as it needs; chart: a Vega-Lite JSON spec with inline data.values (bar, line, area, point, arc…), no URLs; widget: an interactive explorable (self-contained HTML, CSS and inline JS: sliders, toggles, a small simulation; optional first line <!-- size: 640x420 -->), run sandboxed with no network.")] CardKind Kind,
    [property: Description("Card title, 1-120 characters. One idea per card.")] string Title,
    [property: Description("Markdown body. Keep it short (under ~600 characters) except for notes and code. Max 8000 (16000 for code).")] string? Body = null,
    [property: Description("Optional temporary id for this call (e.g. \"c1\"), for connections to cards created in the same call.")] string? Ref = null,
    [property: Description("Required for link cards: the http(s) URL. The server never fetches it; summarize it in the body.")] string? Url = null,
    [property: Description("For code cards: the language, e.g. \"csharp\".")] string? Language = null,
    [property: Description("key marks a central idea.")] Emphasis? Emphasis = null,
    [property: Description("Optional quiet tint.")] Tint? Tint = null,
    [property: Description("Draw the card as a diagram shape: rectangle (step, component), rounded (service), terminal (start/end), diamond (decision), circle (event), ellipse, cylinder (database/store), parallelogram (input/output), hexagon (preparation), document, cloud (external system), person (user/actor), queue (queue/stream/topic), subroutine (predefined process), text (bare label). In a diagram region the shape is sized to its title.")] Shape? Shape = null);

public sealed record ConnectionInput(
    [property: Description("Source: a ref from this call or a card/region id.")] string From,
    [property: Description("Target: a ref from this call or a card/region id.")] string To,
    [property: Description("Default relates. leads-to and depends-on are directional; contradicts marks tension; example-of links an instance to a concept.")] ConnectionKind? Kind = null,
    [property: Description("Optional short label, at most 40 characters.")] string? Label = null,
    [property: Description("solid (default), dashed (async, optional, weak), dotted, thick (main path).")] LineStyle? Line = null,
    [property: Description("Arrowheads: end, start, both or none. Default: by kind (leads-to, depends-on and example-of point at the target).")] Arrowheads? Ends = null);

public sealed record CardUpdate(
    [property: Description("The card id (cd_…).")] string CardId,
    string? Title = null,
    [property: Description("Replace the body (markdown). Use appendBody to add to it instead.")] string? Body = null,
    [property: Description("Append a paragraph to the existing body.")] string? AppendBody = null,
    CardKind? Kind = null,
    string? Url = null,
    string? Language = null,
    Emphasis? Emphasis = null,
    Tint? Tint = null,
    [property: Description("A diagram shape, or none to make it a plain card again.")] Shape? Shape = null,
    [property: Description("A region (id or title) that explains this card in detail, opened when the owner zooms into the card; none removes the link.")] string? Detail = null);

public sealed record ConnectionUpdate(
    [property: Description("The connection id (cx_…), as listed in get_canvas or get_cards.")] string ConnectionId,
    ConnectionKind? Kind = null,
    [property: Description("New label; an empty string removes it.")] string? Label = null,
    LineStyle? Line = null,
    Arrowheads? Ends = null);

public sealed record StepInput(
    [property: Description("What to say at this step: one idea, 1-3 sentences of markdown (at most 2000 characters). Say what happens and why, not what is on screen.")] string Text,
    [property: Description("What to light up while saying it (everything else fades): card titles, sub-region titles, ids (cd_…, rg_…, cx_…), or, in add_diagram, the Mermaid node ids. Connections between two lit items light up and animate by themselves.")] IReadOnlyList<string>? Highlight = null);

public sealed record ConnectionPair(string From, string To, ConnectionKind? Kind = null);

public sealed record ReorganizeAction(
    [property: Description("moveCards {cardIds, toRegion, createIfMissing}; createRegion {title, ref?, parent?, layout?}; renameRegion {regionId, title}; nestRegion {regionId, parentId (omit to make top-level)}; mergeRegions {sourceIds, intoId}; mergeCards {cardIds, intoId, title?, body?}; deleteEmptyRegion {regionId}; setLayout {regionId, layout} turns a region into a diagram (flow-down / flow-right) or back into cards; deleteWalkthrough {intoId: the walkthrough id wt_…}.")]
    ReorganizeType Type,
    IReadOnlyList<string>? CardIds = null,
    [property: Description("Target region: ref, id or title.")] string? ToRegion = null,
    bool CreateIfMissing = false,
    string? Ref = null,
    string? Title = null,
    string? Parent = null,
    string? RegionId = null,
    string? ParentId = null,
    IReadOnlyList<string>? SourceIds = null,
    string? IntoId = null,
    string? Body = null,
    RegionLayout? Layout = null);

internal static class Map
{
    public static string Kind(CardKind kind) => kind switch
    {
        CardKind.Idea => CardKinds.Idea,
        CardKind.Note => CardKinds.Note,
        CardKind.Question => CardKinds.Question,
        CardKind.Link => CardKinds.Link,
        CardKind.Mermaid => CardKinds.Mermaid,
        CardKind.Table => CardKinds.Table,
        CardKind.Chart => CardKinds.Chart,
        CardKind.Widget => CardKinds.Widget,
        _ => CardKinds.Code
    };

    public static string Kind(ConnectionKind kind) => kind switch
    {
        ConnectionKind.LeadsTo => ConnectionKinds.LeadsTo,
        ConnectionKind.Contradicts => ConnectionKinds.Contradicts,
        ConnectionKind.ExampleOf => ConnectionKinds.ExampleOf,
        ConnectionKind.DependsOn => ConnectionKinds.DependsOn,
        _ => ConnectionKinds.Relates
    };

    public static string? Tint(Tint? tint) => tint?.ToString().ToLowerInvariant();
    public static string? Shape(Shape? shape) => shape?.ToString().ToLowerInvariant();
    public static string? Line(LineStyle? line) => line?.ToString().ToLowerInvariant();
    public static string? Ends(Arrowheads? ends) => ends?.ToString().ToLowerInvariant();
    public static string? Layout(RegionLayout? layout) => layout switch
    {
        null => null,
        RegionLayout.FlowDown => RegionLayouts.FlowDown,
        RegionLayout.FlowRight => RegionLayouts.FlowRight,
        _ => RegionLayouts.Cards
    };
    public static string? Emphasis(Emphasis? emphasis) => emphasis?.ToString().ToLowerInvariant();
    public static string Depth(OutlineDepth depth) => depth.ToString().ToLowerInvariant();
    public static string Duplicate(DuplicatePolicy policy) => policy.ToString().ToLowerInvariant();

    public static IEnumerable<Intent> Additions(IReadOnlyList<RegionInput>? regions, IReadOnlyList<CardInput>? cards, IReadOnlyList<ConnectionInput>? connections)
    {
        foreach (var r in regions ?? []) yield return new AddRegionIntent(r.Title, r.Ref, r.Parent, Tint(r.Tint), Layout: Layout(r.Layout));
        foreach (var c in cards ?? []) yield return new AddCardIntent(Kind(c.Kind), c.Title, c.Region, c.Ref, c.Body, c.Url, c.Language, Emphasis(c.Emphasis), Tint(c.Tint), Shape: Shape(c.Shape));
        foreach (var e in connections ?? []) yield return new ConnectIntent(e.From, e.To, e.Kind is null ? null : Kind(e.Kind.Value), e.Label, Line(e.Line), Ends(e.Ends));
    }

    public static Intent Reorganize(ReorganizeAction a) => a.Type switch
    {
        ReorganizeType.MoveCards => new MoveCardsIntent(Need(a.CardIds, "cardIds"), Need(a.ToRegion, "toRegion"), a.CreateIfMissing),
        ReorganizeType.CreateRegion => new AddRegionIntent(Need(a.Title, "title"), a.Ref, a.Parent, Layout: Layout(a.Layout)),
        ReorganizeType.DeleteWalkthrough => new DeleteWalkthroughIntent(Need(a.IntoId, "intoId")),
        ReorganizeType.SetLayout => new UpdateRegionIntent(Need(a.RegionId, "regionId"), Layout: Layout(a.Layout) ?? throw new ArgumentException("setLayout needs layout.")),
        ReorganizeType.RenameRegion => new UpdateRegionIntent(Need(a.RegionId, "regionId"), Need(a.Title, "title")),
        ReorganizeType.NestRegion => new NestRegionIntent(Need(a.RegionId, "regionId"), a.ParentId),
        ReorganizeType.MergeRegions => new MergeRegionsIntent(Need(a.SourceIds, "sourceIds"), Need(a.IntoId, "intoId")),
        ReorganizeType.MergeCards => new MergeCardsIntent(Need(a.CardIds, "cardIds"), Need(a.IntoId, "intoId"), a.Title, a.Body),
        ReorganizeType.DeleteEmptyRegion => new DeleteRegionIntent(Need(a.RegionId, "regionId")),
        _ => throw new ArgumentException("Unknown reorganize action.")
    };

    private static T Need<T>(T? value, string name) where T : class =>
        value is null || (value is string s && string.IsNullOrWhiteSpace(s)) || (value is IReadOnlyList<string> list && list.Count == 0)
            ? throw new ArgumentException($"This action needs {name}.")
            : value;

    public static void Count(int count, int max, string what)
    {
        if (count == 0) throw new ArgumentException($"Give at least one {what}.");
        if (count > max) throw new ArgumentException($"At most {max} {what} per call; split the work into several calls.");
    }
}
