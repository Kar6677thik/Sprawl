using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>Undo and revert. Both produce new operations; history itself is never rewritten.</summary>
public static class HistoryEngine
{
    public static CanvasState Replay(CanvasState start, IEnumerable<Op> ops)
    {
        var state = start.Clone();
        foreach (var op in ops) StateOps.Apply(state, op);
        return state;
    }

    /// <summary>
    /// Selective undo of one batch: every entity it touched must still equal that batch's after-image, and the result must
    /// stay referentially sound (e.g. no later connection to a card the undo would remove).
    /// </summary>
    public static List<Op> Undo(CanvasState current, IReadOnlyList<Op> batchOps)
    {
        var conflicts = batchOps.Where(op => !Equals(current.Find(op.EntityId), op.After)).Select(op => op.EntityId).ToList();
        if (conflicts.Count > 0)
            throw SprawlException.Conflict("Later changes touched the same items; undo them first or revert to an earlier version.", conflicts);
        var target = current.Clone();
        foreach (var op in batchOps) StateOps.Set(target, op.EntityId, op.Before);
        var broken = StateOps.IntegrityProblems(target);
        if (broken.Count > 0)
            throw SprawlException.Conflict("Undoing this would leave later items pointing at removed ones; revert to an earlier version instead.", broken);
        return StateOps.Diff(current, target);
    }

    /// <summary>Operations that turn the current state into the state at an earlier version.</summary>
    public static List<Op> Revert(CanvasState current, CanvasState target) => StateOps.Diff(current, target);
}

public static class Summaries
{
    public static string Describe(IReadOnlyList<Op> ops, CanvasState before, CanvasState after, string kind)
    {
        if (ops.Count == 0) return "No changes";
        int Count(string type) => ops.Count(op => op.Type == type);
        var parts = new List<string>();
        var created = ops.Where(op => op.Type == "card.create").Select(op => (Card)op.After!).ToList();
        if (created.Count > 0)
        {
            var regions = created.Select(card => card.RegionId).Distinct().ToList();
            var where = regions.Count == 1 && regions[0] is { } regionId && after.Regions.TryGetValue(regionId, out var region) ? $" in {region.Title}" : "";
            parts.Add($"+{Plural(created.Count, "card")}{where}");
        }
        Add(parts, Count("region.create"), "+", "region");
        Add(parts, Count("connection.create"), "+", "connection");
        Add(parts, Count("card.update"), "edited ", "card");
        Add(parts, Count("region.update"), "edited ", "region");
        if (ops.Any(op => op.Type == "canvas.update")) parts.Add("canvas details");
        // Cards that changed region or were dragged into place; cards and regions merely pushed aside are not "moved".
        var moved = ops.Count(op => op is { Type: "card.layout", Before: Card b, After: Card a } &&
            (b.RegionId != a.RegionId || (a.Placement == Placements.Manual && (b.X != a.X || b.Y != a.Y || b.Placement != a.Placement))));
        Add(parts, moved, "moved ", "card");
        Add(parts, Count("card.delete"), "-", "card");
        Add(parts, Count("region.delete"), "-", "region");
        Add(parts, Count("connection.delete"), "-", "connection");
        Add(parts, Count("sketch.create"), "+", "sketch");
        Add(parts, Count("sketch.update"), "moved ", "sketch");
        Add(parts, Count("sketch.delete"), "-", "sketch");
        if (kind == BatchKinds.Tidy) return $"Tidied {Plural(ops.Count, "item")}";
        if (parts.Count == 0) parts.Add($"layout ({Plural(ops.Count, "item")})");
        return string.Join(", ", parts);
    }

    private static void Add(List<string> parts, int count, string prefix, string noun)
    {
        if (count > 0) parts.Add(prefix + Plural(count, noun));
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
