namespace Sprawl.Contracts;

public static class CardKinds
{
    public const string Idea = "idea";
    public const string Note = "note";
    public const string Question = "question";
    public const string Link = "link";
    public const string Code = "code";
    public static readonly string[] All = [Idea, Note, Question, Link, Code];
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
}
