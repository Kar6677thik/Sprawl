using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// Getting canvases in and out: Markdown (a readable document, regions as headings, <c>[[links]]</c> kept so it drops
/// into Obsidian) and JSON Canvas 1.0 in both directions. Import produces ordinary intents, so an imported canvas is one
/// create batch, placed by the same engine as everything else.
/// </summary>
public static partial class Exchange
{
    /// <summary>Import may hold one intent per card, region and connection a canvas can have.</summary>
    public const int MaxImportIntents = Limits.CardsPerCanvas + Limits.RegionsPerCanvas + Limits.ConnectionsPerCanvas;

    public const string UnsortedRegion = "Unsorted";

    // JSON Canvas preset colours: 1 red, 2 orange, 3 yellow, 4 green, 5 cyan, 6 purple.
    private static readonly Dictionary<string, string> TintToColor = new(StringComparer.Ordinal)
    {
        ["red"] = "1", ["amber"] = "3", ["green"] = "4", ["blue"] = "5", ["violet"] = "6"
    };
    private static readonly Dictionary<string, string> ColorToTint = new(StringComparer.Ordinal)
    {
        ["1"] = "red", ["2"] = "amber", ["3"] = "amber", ["4"] = "green", ["5"] = "blue", ["6"] = "violet"
    };

    public static string Markdown(CanvasState state)
    {
        var meta = state.Meta ?? throw SprawlException.NotFound("The canvas has no metadata.");
        var md = new StringBuilder();
        md.Append("# ").Append(meta.Title).Append("\n\n");
        if (meta.Description.Length > 0) md.Append("> ").Append(meta.Description.ReplaceLineEndings(" ")).Append("\n\n");
        if (meta.Tags.Count > 0) md.Append("Tags: ").AppendJoin(", ", meta.Tags).Append("\n\n");

        void Cards(IEnumerable<Card> cards)
        {
            foreach (var card in OutlineRenderer.ReadingOrder(cards))
            {
                var title = card.Kind == CardKinds.Link && card.Url is not null ? $"[{card.Title}]({card.Url})" : card.Title;
                md.Append("- ").Append(card.Kind == CardKinds.Question ? "**Q:** " : "").Append("**").Append(title).Append("**")
                    .Append(card.Emphasis == Emphases.Key ? " ★" : "").Append('\n');
                if (card.Body.Length == 0) continue;
                var body = card.Kind == CardKinds.Code ? $"```{card.Language}\n{card.Body}\n```" : card.Body;
                foreach (var line in body.ReplaceLineEndings("\n").Split('\n')) md.Append(line.Length == 0 ? "" : "  ").Append(line).Append('\n');
            }
            md.Append('\n');
        }

        void Section(Region region, int level)
        {
            md.Append(new string('#', level)).Append(' ').Append(region.Title).Append("\n\n");
            Cards(state.CardsIn(region.Id));
            foreach (var sub in OutlineRenderer.ReadingOrder(state.RegionsIn(region.Id))) Section(sub, level + 1);
        }

        foreach (var region in OutlineRenderer.ReadingOrder(state.RegionsIn(null))) Section(region, 2);
        if (state.CardsIn(null).Any())
        {
            md.Append("## Loose cards\n\n");
            Cards(state.CardsIn(null));
        }
        if (state.Connections.Count > 0)
        {
            md.Append("## Connections\n\n");
            foreach (var c in state.Connections.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
                md.Append("- ").Append(TitleOf(state, c.SourceId)).Append(" —").Append(c.Kind).Append("→ ").Append(TitleOf(state, c.TargetId))
                    .Append(c.Label is null ? "" : $" ({c.Label})").Append('\n');
        }
        return md.ToString().TrimEnd() + "\n";
    }

    public static JsonCanvasDocument ToJsonCanvas(CanvasState state)
    {
        var nodes = new List<JsonCanvasNode>();
        // Groups first so they sit beneath their cards in viewers that paint in order.
        foreach (var region in OutlineRenderer.ReadingOrder(state.RegionsIn(null)).SelectMany(r => (IEnumerable<Region>)[r, .. OutlineRenderer.ReadingOrder(state.RegionsIn(r.Id))]))
        {
            var (x, y) = Absolute(state, region.Id);
            nodes.Add(new(region.Id, "group", x, y, region.W, region.H, Color(region.Tint), Label: region.Title));
        }
        foreach (var card in state.Cards.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            var (x, y) = Absolute(state, card.Id);
            nodes.Add(new(card.Id, "text", x, y, card.W, card.H, Color(card.Tint), Text: CardText(card), SprawlKind: card.Kind));
        }
        var edges = state.Connections.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).Select(c => new JsonCanvasEdge(
            c.Id, c.SourceId, c.TargetId, ToEnd: c.Kind is ConnectionKinds.Relates or ConnectionKinds.Contradicts ? "none" : "arrow",
            Color: c.Kind == ConnectionKinds.Contradicts ? "1" : null, Label: c.Label ?? (c.Kind == ConnectionKinds.Relates ? null : c.Kind)));
        return new(nodes, [.. edges]);
    }

    private static string CardText(Card card)
    {
        var heading = card.Kind == CardKinds.Link && card.Url is not null ? $"## [{card.Title}]({card.Url})" : $"## {card.Title}";
        var body = card.Kind == CardKinds.Code ? $"```{card.Language}\n{card.Body}\n```" : card.Body;
        return body.Length == 0 ? heading : heading + "\n\n" + body;
    }

    /// <summary>
    /// A JSON Canvas as create intents. Groups become regions (a group inside another becomes a sub-region; deeper ones
    /// flatten to the nearest allowed level), each node joins the smallest group containing its centre, nodes outside any
    /// group go to <see cref="UnsortedRegion"/>, and edges become connections (an edge label that names a kind sets it).
    /// </summary>
    public static IReadOnlyList<Intent> FromJsonCanvas(JsonCanvasDocument canvas, List<string>? warnings = null)
    {
        warnings ??= [];
        var nodes = (canvas.Nodes ?? []).Where(n => n.Id.Length > 0).DistinctBy(n => n.Id).ToList();
        var groups = nodes.Where(n => n.Type == "group").OrderByDescending(n => n.Width * n.Height).ThenBy(n => n.Y).ThenBy(n => n.X).ToList();
        var intents = new List<Intent>();
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        var regionRef = new Dictionary<string, string>(StringComparer.Ordinal);
        var parentOf = new Dictionary<string, string?>(StringComparer.Ordinal);
        var usedTitles = new HashSet<string>(StringComparer.Ordinal);

        JsonCanvasNode? Container(JsonCanvasNode node) => groups
            .Where(g => g.Id != node.Id && g.Width * g.Height > node.Width * node.Height && Contains(g, node.X + node.Width / 2, node.Y + node.Height / 2))
            .OrderBy(g => g.Width * g.Height).FirstOrDefault();

        // Largest groups first, so every parent region exists before its children.
        foreach (var group in groups)
        {
            var parent = Container(group);
            // Sprawl nests one level: a group inside a sub-region joins that sub-region's parent instead.
            while (parent is not null && parentOf.GetValueOrDefault(parent.Id) is { } grand) parent = groups.First(g => g.Id == grand);
            parentOf[group.Id] = parent?.Id;
            var title = Unique(Clip(string.IsNullOrWhiteSpace(group.Label) ? "Group" : group.Label!, Limits.RegionTitle), usedTitles, parent?.Id);
            var reference = "g" + intents.Count.ToString(CultureInfo.InvariantCulture);
            regionRef[group.Id] = reference;
            refs[group.Id] = reference;
            intents.Add(new AddRegionIntent(title, reference, parent is null ? null : regionRef[parent.Id], Tint(group.Color)));
        }

        var unsorted = false;
        foreach (var node in nodes.Where(n => n.Type != "group").OrderBy(n => n.Y).ThenBy(n => n.X))
        {
            var card = CardFrom(node);
            if (card is null) { warnings.Add($"Skipped empty node {node.Id}."); continue; }
            var container = Container(node);
            string region;
            if (container is not null) region = regionRef[container.Id];
            else
            {
                if (!unsorted) { intents.Insert(0, new AddRegionIntent(UnsortedRegion, "unsorted")); unsorted = true; }
                region = "unsorted";
            }
            var reference = "n" + intents.Count.ToString(CultureInfo.InvariantCulture);
            refs[node.Id] = reference;
            intents.Add(card with { Region = region, Ref = reference, Tint = Tint(node.Color) });
        }

        foreach (var edge in canvas.Edges ?? [])
        {
            if (!refs.TryGetValue(edge.FromNode, out var from) || !refs.TryGetValue(edge.ToNode, out var to) || from == to)
            {
                warnings.Add($"Skipped edge {edge.Id}: an end is missing or empty.");
                continue;
            }
            var label = edge.Label?.Trim();
            var kind = label is not null && ConnectionKinds.All.Contains(label.ToLowerInvariant(), StringComparer.Ordinal) ? label.ToLowerInvariant() : ConnectionKinds.Relates;
            if (kind != ConnectionKinds.Relates) label = null;
            else if (edge.Color == "1" && string.IsNullOrEmpty(label)) kind = ConnectionKinds.Contradicts;
            intents.Add(new ConnectIntent(from, to, kind, string.IsNullOrEmpty(label) ? null : Clip(label, Limits.Label)));
        }
        if (intents.Count > MaxImportIntents) throw SprawlException.Limit($"An import holds at most {MaxImportIntents} nodes and edges.");
        return intents;
    }

    /// <summary>A card from a node: the first line of text is the title (a heading or bold marker is dropped).</summary>
    private static AddCardIntent? CardFrom(JsonCanvasNode node)
    {
        switch (node.Type)
        {
            case "link" when Uri.TryCreate(node.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https":
                return new AddCardIntent(CardKinds.Link, Clip((uri.Host + uri.AbsolutePath).TrimEnd('/'), Limits.CardTitle), Url: uri.ToString());
            case "file" when !string.IsNullOrWhiteSpace(node.File):
                return new AddCardIntent(CardKinds.Note, Clip(Path.GetFileNameWithoutExtension(node.File!.Replace('\\', '/').Split('/')[^1]), Limits.CardTitle), Body: $"File: `{node.File}`");
            case "text" when !string.IsNullOrWhiteSpace(node.Text):
                break;
            default:
                return null;
        }
        var lines = node.Text!.ReplaceLineEndings("\n").Trim().Split('\n');
        var first = HeadingMarker().Replace(lines[0].Trim(), "").Trim();
        if (first.StartsWith("**", StringComparison.Ordinal) && first.EndsWith("**", StringComparison.Ordinal) && first.Length > 4) first = first[2..^2].Trim();
        var rest = string.Join('\n', lines.Skip(1)).Trim('\n');
        string? url = null;
        var kind = CardKinds.Note;
        if (MarkdownLink().Match(first) is { Success: true } link && Uri.TryCreate(link.Groups[2].Value, UriKind.Absolute, out var linkUri) && linkUri.Scheme is "http" or "https")
        {
            first = link.Groups[1].Value;
            url = linkUri.ToString();
            kind = CardKinds.Link;
        }
        string? language = null;
        if (kind == CardKinds.Note && FencedOnly().Match(rest) is { Success: true } code)
        {
            kind = CardKinds.Code;
            language = code.Groups[1].Value.Length > 0 ? code.Groups[1].Value.ToLowerInvariant() : null;
            rest = code.Groups[2].Value;
        }
        if (first.Length == 0) first = "Untitled";
        if (first.Length > Limits.CardTitle)
        {
            rest = first + (rest.Length > 0 ? "\n\n" + rest : "");
            first = Clip(first, Limits.CardTitle);
        }
        if (kind == CardKinds.Note && first.EndsWith('?')) kind = CardKinds.Question;
        if (kind == CardKinds.Note && node.SprawlKind is CardKinds.Idea or CardKinds.Question) kind = node.SprawlKind;
        var max = kind == CardKinds.Code ? Limits.CodeBody : Limits.CardBody;
        if (rest.Length > max) rest = rest[..(max - 1)] + "…";
        return new AddCardIntent(kind, first, Body: rest.Length > 0 ? rest : null, Url: url, Language: language);
    }

    private static string Unique(string title, HashSet<string> used, string? parent)
    {
        var candidate = title;
        for (var i = 2; !used.Add((parent ?? "") + "/" + Text.Normalize(candidate)); i++) candidate = Clip($"{title} ({i})", Limits.RegionTitle);
        return candidate;
    }

    private static bool Contains(JsonCanvasNode group, double x, double y) => x >= group.X && x <= group.X + group.Width && y >= group.Y && y <= group.Y + group.Height;

    private static string Clip(string text, int max)
    {
        var line = string.Join(' ', text.Split((char[])['\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }

    private static string? Color(string? tint) => tint is null ? null : TintToColor.GetValueOrDefault(tint);
    private static string? Tint(string? color) => color is null ? null : ColorToTint.GetValueOrDefault(color);

    private static (int X, int Y) Absolute(CanvasState state, string id)
    {
        var (x, y, parent) = state.Find(id) switch
        {
            Card c => (c.X, c.Y, c.RegionId),
            Region r => (r.X, r.Y, r.ParentId),
            _ => (0, 0, (string?)null)
        };
        while (parent is not null && state.Regions.TryGetValue(parent, out var region))
        {
            x += region.X;
            y += region.Y;
            parent = region.ParentId;
        }
        return (x, y);
    }

    private static string TitleOf(CanvasState state, string id) => state.Find(id) switch
    {
        Card card => card.Title,
        Region region => region.Title,
        _ => id
    };

    [GeneratedRegex(@"^#{1,6}\s+")] private static partial Regex HeadingMarker();
    [GeneratedRegex(@"^\[([^\]]+)\]\(([^)\s]+)\)$")] private static partial Regex MarkdownLink();
    [GeneratedRegex(@"^```([A-Za-z0-9+#.-]*)\n([\s\S]*?)\n```$")] private static partial Regex FencedOnly();
}
