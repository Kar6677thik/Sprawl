using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// One resolved change to one entity: full before and after images (null before = create, null after = delete).
/// Positions are already computed, so replaying ops reproduces a state exactly without re-running placement.
/// </summary>
public sealed record Op(string Type, string EntityId, IEntity? Before, IEntity? After)
{
    public Op Inverse() => Create(EntityId, After, Before);

    public static Op Create(string id, IEntity? before, IEntity? after) => new(TypeOf(id, before, after), id, before, after);

    private static string TypeOf(string id, IEntity? before, IEntity? after)
    {
        var noun = EntityIds.KindOf(id) switch
        {
            EntityKind.Canvas => "canvas",
            EntityKind.Region => "region",
            EntityKind.Card => "card",
            EntityKind.Connection => "connection",
            _ => throw new ArgumentException($"Unknown entity id '{id}'.")
        };
        if (before is null) return noun + ".create";
        if (after is null) return noun + ".delete";
        return (before, after) switch
        {
            (Card b, Card a) when b with { X = a.X, Y = a.Y, W = a.W, H = a.H, RegionId = a.RegionId, Placement = a.Placement, Size = a.Size, SizeSource = a.SizeSource } == a => "card.layout",
            (Region b, Region a) when b with { X = a.X, Y = a.Y, W = a.W, H = a.H, Placement = a.Placement, Cols = a.Cols } == a => "region.layout",
            _ => noun + ".update"
        };
    }
}

public static class StateOps
{
    /// <summary>Set the entity to the op's after-image.</summary>
    public static void Apply(CanvasState state, Op op) => Set(state, op.EntityId, op.After);

    public static void Set(CanvasState state, string id, IEntity? value)
    {
        switch (EntityIds.KindOf(id))
        {
            case EntityKind.Canvas: state.Meta = (CanvasMeta?)value; break;
            case EntityKind.Region: if (value is Region region) state.Regions[id] = region; else state.Regions.Remove(id); break;
            case EntityKind.Card: if (value is Card card) state.Cards[id] = card; else state.Cards.Remove(id); break;
            case EntityKind.Connection: if (value is Connection connection) state.Connections[id] = connection; else state.Connections.Remove(id); break;
            default: throw new ArgumentException($"Unknown entity id '{id}'.");
        }
    }

    /// <summary>Every entity that differs between two states, as ops turning <paramref name="from"/> into <paramref name="to"/>.</summary>
    public static List<Op> Diff(CanvasState from, CanvasState to)
    {
        var ops = new List<Op>();
        if (!Equals(from.Meta, to.Meta)) ops.Add(Op.Create((to.Meta ?? from.Meta)!.Id, from.Meta, to.Meta));
        DiffSet(from.Regions, to.Regions, ops);
        DiffSet(from.Cards, to.Cards, ops);
        DiffSet(from.Connections, to.Connections, ops);
        return ops;
    }

    private static void DiffSet<T>(Dictionary<string, T> from, Dictionary<string, T> to, List<Op> ops) where T : class, IEntity
    {
        foreach (var id in from.Keys.Union(to.Keys).Order(StringComparer.Ordinal))
        {
            var before = from.GetValueOrDefault(id);
            var after = to.GetValueOrDefault(id);
            if (!Equals(before, after)) ops.Add(Op.Create(id, before, after));
        }
    }

    /// <summary>Referential problems: cards in missing regions, sub-regions of missing or nested parents, dangling connections.</summary>
    public static List<string> IntegrityProblems(CanvasState state)
    {
        var problems = new List<string>();
        foreach (var card in state.Cards.Values)
            if (card.RegionId is not null && !state.Regions.ContainsKey(card.RegionId)) problems.Add(card.Id);
        foreach (var region in state.Regions.Values)
            if (region.ParentId is not null && (!state.Regions.TryGetValue(region.ParentId, out var parent) || parent.ParentId is not null))
                problems.Add(region.Id);
        foreach (var connection in state.Connections.Values)
            if (state.Find(connection.SourceId) is null || state.Find(connection.TargetId) is null) problems.Add(connection.Id);
        return problems;
    }
}
