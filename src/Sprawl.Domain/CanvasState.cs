using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>The complete current state of one canvas. Entities are immutable records; the dictionaries are the working set.</summary>
public sealed class CanvasState
{
    public CanvasMeta? Meta { get; set; }
    public Dictionary<string, Region> Regions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Card> Cards { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Connection> Connections { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Sketch> Sketches { get; } = new(StringComparer.Ordinal);

    public CanvasState Clone()
    {
        var copy = new CanvasState { Meta = Meta };
        foreach (var (id, region) in Regions) copy.Regions[id] = region;
        foreach (var (id, card) in Cards) copy.Cards[id] = card;
        foreach (var (id, connection) in Connections) copy.Connections[id] = connection;
        foreach (var (id, sketch) in Sketches) copy.Sketches[id] = sketch;
        return copy;
    }

    public CanvasSnapshot ToSnapshot() => new(
        Meta,
        [.. Regions.Values.OrderBy(region => region.Id, StringComparer.Ordinal)],
        [.. Cards.Values.OrderBy(card => card.Id, StringComparer.Ordinal)],
        [.. Connections.Values.OrderBy(connection => connection.Id, StringComparer.Ordinal)],
        [.. Sketches.Values.OrderBy(sketch => sketch.Id, StringComparer.Ordinal)]);

    public static CanvasState FromSnapshot(CanvasSnapshot snapshot)
    {
        var state = new CanvasState { Meta = snapshot.Meta };
        foreach (var region in snapshot.Regions) state.Regions[region.Id] = region;
        foreach (var card in snapshot.Cards) state.Cards[card.Id] = card;
        foreach (var connection in snapshot.Connections) state.Connections[connection.Id] = connection;
        foreach (var sketch in snapshot.Sketches ?? []) state.Sketches[sketch.Id] = sketch;
        return state;
    }

    public IEntity? Find(string id) => EntityIds.KindOf(id) switch
    {
        EntityKind.Canvas => Meta is not null && Meta.Id == id ? Meta : null,
        EntityKind.Region => Regions.GetValueOrDefault(id),
        EntityKind.Card => Cards.GetValueOrDefault(id),
        EntityKind.Connection => Connections.GetValueOrDefault(id),
        EntityKind.Sketch => Sketches.GetValueOrDefault(id),
        _ => null
    };

    public IEnumerable<Card> CardsIn(string? regionId) => Cards.Values.Where(card => card.RegionId == regionId);
    public IEnumerable<Region> RegionsIn(string? parentId) => Regions.Values.Where(region => region.ParentId == parentId);
}

/// <param name="Sketches">Absent in snapshots written before sketches existed.</param>
public sealed record CanvasSnapshot(CanvasMeta? Meta, IReadOnlyList<Region> Regions, IReadOnlyList<Card> Cards, IReadOnlyList<Connection> Connections, IReadOnlyList<Sketch>? Sketches = null);

public enum EntityKind { Unknown, Canvas, Region, Card, Connection, Sketch }

public static class EntityIds
{
    public const string CanvasPrefix = "cv_";
    public const string RegionPrefix = "rg_";
    public const string CardPrefix = "cd_";
    public const string ConnectionPrefix = "cx_";
    public const string SketchPrefix = "sk_";
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz"; // Crockford base32, lowercase

    public static EntityKind KindOf(string id) => id.Length < 4 ? EntityKind.Unknown : id[..3] switch
    {
        CanvasPrefix => EntityKind.Canvas,
        RegionPrefix => EntityKind.Region,
        CardPrefix => EntityKind.Card,
        ConnectionPrefix => EntityKind.Connection,
        SketchPrefix => EntityKind.Sketch,
        _ => EntityKind.Unknown
    };

    public static string Prefix(EntityKind kind) => kind switch
    {
        EntityKind.Canvas => CanvasPrefix,
        EntityKind.Region => RegionPrefix,
        EntityKind.Card => CardPrefix,
        EntityKind.Connection => ConnectionPrefix,
        EntityKind.Sketch => SketchPrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string Random(EntityKind kind)
    {
        Span<byte> bytes = stackalloc byte[5];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        ulong value = 0;
        foreach (var b in bytes) value = (value << 8) | b;
        Span<char> chars = stackalloc char[8];
        for (var i = 7; i >= 0; i--) { chars[i] = Alphabet[(int)(value & 31)]; value >>= 5; }
        return Prefix(kind) + new string(chars);
    }

    public static bool IsWellFormed(string id) =>
        id.Length == 11 && KindOf(id) != EntityKind.Unknown && id.AsSpan(3).IndexOfAnyExcept(Alphabet) < 0;
}

/// <summary>Creates entity IDs. Random in production; sequential in tests so layouts are reproducible.</summary>
public interface IIdGenerator
{
    string Next(EntityKind kind);
}

public sealed class RandomIdGenerator : IIdGenerator
{
    public static readonly RandomIdGenerator Instance = new();
    public string Next(EntityKind kind) => EntityIds.Random(kind);
}
