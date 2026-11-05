using System.Text;
using Sprawl.Contracts;

namespace Sprawl.Domain;

public static class OutlineDepths
{
    public const string Regions = "regions";
    public const string Titles = "titles";
    public const string Short = "short";
    public const string Full = "full";
    public static readonly string[] All = [Regions, Titles, Short, Full];
}

public sealed record OutlineOptions(string Depth = OutlineDepths.Short, string? Region = null, bool IncludeConnections = true, int MaxCards = 400);

/// <summary>
/// The token-efficient text view of a canvas for Claude: regions in reading order, cards with ids, kinds, titles and
/// optional bodies, then connections. Never coordinates.
/// </summary>
public static class OutlineRenderer
{
    public const int ShortBody = 140;

    public static string Render(CanvasState state, long version, OutlineOptions options)
    {
        var meta = state.Meta ?? throw SprawlException.NotFound("The canvas does not exist.");
        var depth = Text.OneOf(options.Depth, OutlineDepths.All, "depth");
        var maxCards = Math.Clamp(options.MaxCards, 1, 1000);
        Region? focus = null;
        if (options.Region is not null)
        {
            focus = state.Regions.GetValueOrDefault(options.Region)
                ?? state.Regions.Values.Where(region => Text.SameTitle(region.Title, options.Region)).OrderBy(region => region.ParentId is null ? 0 : 1).FirstOrDefault()
                ?? throw SprawlException.NotFound($"Region '{options.Region}' was not found.");
        }

        var output = new StringBuilder();
        output.Append("# ").Append(meta.Title).Append("  ").Append(meta.Id).Append("  v").Append(version);
        if (meta.Tags.Count > 0) output.Append("  tags: ").Append(string.Join(", ", meta.Tags));
        if (meta.Collection is not null) output.Append("  collection: ").Append(meta.Collection);
        if (meta.Archived) output.Append("  archived");
        output.Append('\n');
        if (meta.Description.Length > 0) output.Append("> ").Append(OneLine(meta.Description)).Append('\n');
        var questions = state.Cards.Values.Count(card => card.Kind == CardKinds.Question);
        output.Append(state.Regions.Count).Append(" regions · ").Append(state.Cards.Count).Append(" cards · ")
            .Append(questions).Append(" open questions\n");

        var included = new HashSet<string>(StringComparer.Ordinal);
        var emitted = 0;
        var omitted = new Dictionary<string, int>(StringComparer.Ordinal);
        var topLevel = focus is not null ? [focus] : ReadingOrder(state.RegionsIn(null)).ToList();
        foreach (var region in topLevel)
        {
            WriteRegion(region, region.ParentId is null ? 2 : 3);
            if (region.ParentId is null)
                foreach (var sub in ReadingOrder(state.RegionsIn(region.Id))) WriteRegion(sub, 3);
        }
        if (focus is null && state.CardsIn(null).Any())
        {
            output.Append("\n## Loose cards\n");
            WriteCards(state.CardsIn(null), "loose");
        }
        if (omitted.Count > 0)
            output.Append("\n…truncated: ").Append(omitted.Values.Sum()).Append(" more cards in ").Append(omitted.Count)
                .Append(" regions; call get_canvas with region=<id> (e.g. ").Append(omitted.Keys.First()).Append(").\n");

        if (options.IncludeConnections && depth != OutlineDepths.Regions)
        {
            var connections = state.Connections.Values
                .Where(c => included.Contains(c.SourceId) || included.Contains(c.TargetId))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
            if (connections.Count > 0)
            {
                output.Append("\n## Connections\n");
                foreach (var c in connections)
                {
                    output.Append(c.SourceId).Append(" -").Append(c.Kind).Append("-> ").Append(c.TargetId);
                    if (c.Label is not null) output.Append(" \"").Append(c.Label).Append('"');
                    if (c.Line is not null) output.Append(" [").Append(c.Line).Append(']');
                    if (c.Ends is not null) output.Append(" [arrows: ").Append(c.Ends).Append(']');
                    output.Append("  ").Append(c.Id).Append('\n');
                }
            }
        }
        return output.ToString();

        void WriteRegion(Region region, int level)
        {
            included.Add(region.Id);
            var cards = state.CardsIn(region.Id).ToList();
            var sketches = state.Sketches.Values.Count(s => s.RegionId == region.Id);
            output.Append('\n').Append('#', level).Append(' ').Append(region.Title).Append("  ").Append(region.Id)
                .Append("  (").Append(cards.Count).Append(cards.Count == 1 ? " card" : " cards")
                .Append(sketches == 0 ? "" : sketches == 1 ? ", 1 sketch by the owner" : $", {sketches} sketches by the owner")
                .Append(region.Layout switch { RegionLayouts.FlowDown => ", diagram, flows down", RegionLayouts.FlowRight => ", diagram, flows right", _ => "" }).Append(")\n");
            if (depth != OutlineDepths.Regions) WriteCards(cards, region.Id);
            foreach (var walkthrough in state.Walkthroughs.Values.Where(w => w.RegionId == region.Id).OrderBy(w => w.CreatedAt).ThenBy(w => w.Id, StringComparer.Ordinal))
            {
                output.Append("~ walkthrough ").Append(walkthrough.Id).Append(' ').Append(walkthrough.Title).Append(" (").Append(walkthrough.Steps.Count)
                    .Append(walkthrough.Steps.Count == 1 ? " step)" : " steps)").Append('\n');
                if (depth != OutlineDepths.Full) continue;
                foreach (var (step, i) in walkthrough.Steps.Select((s, i) => (s, i + 1)))
                    output.Append("  ").Append(i).Append(". ").Append(OneLine(step.Text))
                        .Append((step.Highlight ?? []).Count == 0 ? "" : " [" + string.Join(", ", step.Highlight!) + "]").Append('\n');
            }
        }

        void WriteCards(IEnumerable<Card> cards, string regionKey)
        {
            foreach (var card in ReadingOrder(cards))
            {
                if (emitted >= maxCards) { omitted[regionKey] = omitted.GetValueOrDefault(regionKey) + 1; continue; }
                emitted++;
                included.Add(card.Id);
                output.Append("- ").Append(card.Id).Append(' ').Append(card.Shape is null ? KindLabel(card.Kind) : $"[{card.Shape}]").Append(' ');
                if (card.Emphasis == Emphases.Key) output.Append("★ ");
                output.Append(card.Title);
                if (card.Url is not null && depth != OutlineDepths.Full) output.Append(" <").Append(card.Url).Append('>');
                if (depth == OutlineDepths.Short && card.Body.Length > 0)
                {
                    var body = OneLine(card.Body);
                    output.Append(" — ").Append(body.Length > ShortBody ? body[..ShortBody].TrimEnd() + "…" : body);
                }
                output.Append('\n');
                if (depth == OutlineDepths.Full)
                {
                    if (card.Url is not null) output.Append("  url: ").Append(card.Url).Append('\n');
                    if (card.Language is not null) output.Append("  language: ").Append(card.Language).Append('\n');
                    foreach (var line in card.Body.Split('\n')) output.Append("  ").Append(line).Append('\n');
                }
            }
        }
    }

    internal static IEnumerable<Region> ReadingOrder(IEnumerable<Region> regions) =>
        regions.OrderBy(region => region.Y).ThenBy(region => region.X).ThenBy(region => region.Id, StringComparer.Ordinal);

    /// <summary>Creation order (the order cards were added), then position for cards created together.</summary>
    internal static IEnumerable<Card> ReadingOrder(IEnumerable<Card> cards) =>
        cards.OrderBy(card => card.CreatedAt).ThenBy(card => card.Y).ThenBy(card => card.X).ThenBy(card => card.Id, StringComparer.Ordinal);

    private static string KindLabel(string kind) => kind switch
    {
        CardKinds.Question => "?",
        CardKinds.Idea => "idea",
        CardKinds.Note => "note",
        CardKinds.Link => "link",
        CardKinds.Code => "code",
        CardKinds.Mermaid => "mermaid",
        _ => kind
    };

    private static string OneLine(string text) => string.Join(' ', text.Split((char[])['\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
