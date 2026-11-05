namespace Sprawl.Contracts;

public static class CardKinds
{
    public const string Idea = "idea";
    public const string Note = "note";
    public const string Question = "question";
    public const string Link = "link";
    public const string Code = "code";
    /// <summary>A rendered Mermaid diagram of any type (sequence, class, ER, state, mind map, timeline, …); the body is the source.</summary>
    public const string Mermaid = "mermaid";
    /// <summary>A markdown table, shown as wide as its columns need.</summary>
    public const string Table = "table";
    /// <summary>A chart: the body is a Vega-Lite JSON spec with inline data.</summary>
    public const string Chart = "chart";
    public static readonly string[] All = [Idea, Note, Question, Link, Code, Mermaid, Table, Chart];
}

public static class ConnectionKinds
{
    public const string Relates = "relates";
    public const string LeadsTo = "leads-to";
    public const string Contradicts = "contradicts";
    public const string ExampleOf = "example-of";
    public const string DependsOn = "depends-on";
    public static readonly string[] All = [Relates, LeadsTo, Contradicts, ExampleOf, DependsOn];
}

public static class Tints
{
    /// <summary>Sent in an update to clear a tint.</summary>
    public const string None = "none";
    public static readonly string[] All = ["grey", "blue", "green", "amber", "red", "violet"];
}

public static class Emphases
{
    public const string Normal = "normal";
    public const string Key = "key";
    public static readonly string[] All = [Normal, Key];
}

/// <summary>Diagram shapes, as in draw.io and Mermaid. A card with a shape draws as that shape; without one it is a card.</summary>
public static class Shapes
{
    public const string None = "none";
    public const string Rectangle = "rectangle";   // a process step, a component
    public const string Rounded = "rounded";       // a step, a service
    public const string Terminal = "terminal";     // start / end (stadium)
    public const string Diamond = "diamond";       // a decision
    public const string Ellipse = "ellipse";
    public const string Circle = "circle";         // an event, a junction
    public const string Cylinder = "cylinder";     // a database, a store
    public const string Parallelogram = "parallelogram"; // input / output
    public const string Hexagon = "hexagon";       // preparation, a condition
    public const string Document = "document";
    public const string Cloud = "cloud";           // an external system, the internet
    public const string Person = "person";         // an actor, a user
    public const string Queue = "queue";           // a queue, a stream, a topic
    public const string Subroutine = "subroutine"; // a predefined process
    public const string Text = "text";             // a bare label
    public static readonly string[] All = [Rectangle, Rounded, Terminal, Diamond, Ellipse, Circle, Cylinder, Parallelogram, Hexagon, Document, Cloud, Person, Queue, Subroutine, Text];
}

/// <summary>How a region arranges its contents: cards in masonry columns, or a diagram laid out as a layered flow.</summary>
public static class RegionLayouts
{
    public const string Cards = "cards";
    public const string FlowDown = "flow-down";
    public const string FlowRight = "flow-right";
    public static readonly string[] All = [Cards, FlowDown, FlowRight];
    public static bool IsDiagram(string? layout) => layout is FlowDown or FlowRight;
}

public static class LineStyles
{
    public const string Solid = "solid";
    public const string Dashed = "dashed";
    public const string Dotted = "dotted";
    public const string Thick = "thick";
    public static readonly string[] All = [Solid, Dashed, Dotted, Thick];
}

/// <summary>Where a connection draws arrowheads. Unset, the kind decides (directional kinds get one at the target).</summary>
public static class EndStyles
{
    public const string None = "none";
    public const string End = "end";
    public const string Start = "start";
    public const string Both = "both";
    public static readonly string[] All = [None, End, Start, Both];
}

public static class Placements
{
    /// <summary>The server places the item and may translate it to make room.</summary>
    public const string Auto = "auto";
    /// <summary>The owner placed the item; nothing but the owner, Tidy or Unpin moves it.</summary>
    public const string Manual = "manual";
}

public static class CardSizes
{
    public const string Compact = "compact";
    public const string Regular = "regular";
    public const string Tall = "tall";
}

public static class Authors
{
    public const string Human = "human";
    public const string Ai = "ai";
}

public static class ActorKinds
{
    public const string Owner = "owner";
    public const string Mcp = "mcp";
}

public static class BatchKinds
{
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Tidy = "tidy";
    public const string Undo = "undo";
    public const string Revert = "revert";
    public const string Trash = "trash";
    public const string Restore = "restore";
}

public static class DuplicatePolicies
{
    public const string Skip = "skip";
    public const string Add = "add";
    public const string Fail = "fail";
}

/// <summary>Stable error codes shared by the API (problem+json <c>code</c>) and the MCP result envelope.</summary>
public static class ErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string NotFound = "not_found";
    public const string StaleVersion = "stale_version";
    public const string PreconditionRequired = "precondition_required";
    public const string PinnedItem = "pinned_item";
    public const string Duplicate = "duplicate";
    public const string LimitExceeded = "limit_exceeded";
    public const string Conflict = "conflict";
    public const string ForbiddenForActor = "forbidden_for_actor";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
}

/// <summary>Size limits enforced by the API and repeated in MCP input validation.</summary>
public static class Limits
{
    public const int CanvasTitle = 120;
    public const int CanvasDescription = 1000;
    public const int CardTitle = 120;
    public const int RegionTitle = 80;
    public const int CardBody = 8000;
    public const int CodeBody = 16000;
    public const int Url = 2048;
    public const int Language = 32;
    public const int Label = 40;
    public const int Collection = 80;
    public const int Tags = 12;
    public const int Tag = 32;
    public const int IntentsPerBatch = 200;
    public const int CardsPerCanvas = 3000;
    public const int RegionsPerCanvas = 300;
    public const int ConnectionsPerCanvas = 6000;
    public const int Ref = 64;
    public const int CardsPerQuery = 50;
    public const int SketchPath = 20000;
    public const int SketchesPerCanvas = 1000;
    public const int SketchSize = 20000;
    public const int Mermaid = 20000;
    public const int DiagramNodes = 300;
    public const int WalkthroughSteps = 40;
    public const int StepText = 2000;
    public const int StepHighlight = 40;
    public const int WalkthroughsPerCanvas = 200;
}
