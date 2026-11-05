using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// Read-only analysis of a canvas: a card's context (links, backlinks, mentions, related cards) and the canvas's
/// structure (gaps between regions, orphans, dangling links, hubs). Deterministic and model-free: similarity is shared
/// key terms weighted by how rare they are on this canvas (tf-idf), so nothing leaves the cluster and nothing is stored.
/// </summary>
public sealed class Insights
{
    public const int MaxRelated = 8;
    public const int MaxGaps = 10;
    public const int MaxHubs = 5;
    public const int MaxListed = 50;
    private const int TermsPerCard = 12;

    private readonly CanvasState state;
    private readonly Semantics.LinkGraph links;
    private readonly Dictionary<string, Dictionary<string, double>> vectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> neighbours = new(StringComparer.Ordinal);

    public Insights(CanvasState state)
    {
        this.state = state;
        links = Semantics.LinkGraph.Build(state);

        var terms = state.Cards.Values.ToDictionary(c => c.Id, c => Semantics.KeyTerms(c.Title, c.Body, TermsPerCard), StringComparer.Ordinal);
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var list in terms.Values)
            foreach (var term in list) documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
        var n = Math.Max(1, state.Cards.Count);
        foreach (var (id, list) in terms)
        {
            // A term on most cards says nothing about any of them; on a tiny canvas everything is kept.
            var vector = new Dictionary<string, double>(StringComparer.Ordinal);
            for (var i = 0; i < list.Count; i++)
            {
                var df = documentFrequency[list[i]];
                if (n >= 6 && df * 2 > n) continue;
                vector[list[i]] = Math.Log(1.0 + (double)n / df) * (1.0 - i * 0.04); // earlier terms are more characteristic
            }
            vectors[id] = vector;
        }
        foreach (var card in state.Cards.Values)
        {
            tags[card.Id] = Semantics.Tags(card);
            neighbours[card.Id] = new(StringComparer.Ordinal);
        }
        foreach (var connection in state.Connections.Values)
        {
            if (neighbours.TryGetValue(connection.SourceId, out var a)) a.Add(connection.TargetId);
            if (neighbours.TryGetValue(connection.TargetId, out var b)) b.Add(connection.SourceId);
        }
        foreach (var card in state.Cards.Values)
            foreach (var target in links.Outgoing(card.Id).Outgoing)
            {
                neighbours[card.Id].Add(target.Id);
                neighbours[target.Id].Add(card.Id);
            }
    }

    public CardRef Ref(Card card) => new(card.Id, card.Title, card.Kind, card.RegionId,
        card.RegionId is { } r && state.Regions.TryGetValue(r, out var region) ? region.Title : null);

    public CardContext Context(string canvasId, long version, Card card)
    {
        var outgoing = links.Outgoing(card.Id);
        return new(canvasId, version, Ref(card), tags[card.Id], [.. outgoing.Outgoing.Select(Ref)], outgoing.Unresolved,
            [.. links.Backlinks(card.Id).Select(Ref)], [.. Semantics.UnlinkedMentions(state, card, links).Take(MaxListed).Select(Ref)], Related(card));
    }

    /// <summary>Cards most like this one that it is not already connected or linked to.</summary>
    public IReadOnlyList<RelatedCard> Related(Card card, int max = MaxRelated)
    {
        var mine = vectors[card.Id];
        var myTags = tags[card.Id];
        var near = neighbours[card.Id];
        var scored = new List<RelatedCard>();
        foreach (var other in state.Cards.Values)
        {
            if (other.Id == card.Id || near.Contains(other.Id)) continue;
            var theirs = vectors[other.Id];
            var shared = mine.Keys.Where(theirs.ContainsKey).OrderByDescending(t => mine[t] + theirs[t]).ThenBy(t => t, StringComparer.Ordinal).ToList();
            var sharedTags = myTags.Intersect(tags[other.Id], StringComparer.Ordinal).ToList();
            var sharedNeighbours = near.Count(neighbours[other.Id].Contains);
            var score = Cosine(mine, theirs) + 0.25 * sharedTags.Count + 0.1 * sharedNeighbours;
            if (score < 0.12 || (shared.Count == 0 && sharedTags.Count == 0 && sharedNeighbours < 2)) continue;
            scored.Add(new(Ref(other), Math.Round(score, 3), [.. sharedTags.Select(t => "#" + t), .. shared.Take(5)]));
        }
        return [.. scored.OrderByDescending(r => r.Score).ThenBy(r => r.Card.Id, StringComparer.Ordinal).Take(max)];
    }

    public CanvasInsights Analyze(string canvasId, long version)
    {
        var cards = state.Cards.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var textLinks = cards.Sum(c => links.Outgoing(c.Id).Outgoing.Count);

        var orphans = cards.Where(c => neighbours[c.Id].Count == 0).Select(Ref);

        var dangling = cards.SelectMany(c => links.Outgoing(c.Id).Unresolved.Select(target => (Target: target, Card: c)))
            .GroupBy(x => Text.Normalize(x.Target), StringComparer.Ordinal)
            .Select(g => new DanglingLink(g.First().Target, [.. g.Select(x => x.Card).DistinctBy(c => c.Id).Select(Ref)]))
            .OrderByDescending(d => d.From.Count).ThenBy(d => d.Target, StringComparer.OrdinalIgnoreCase);

        var questions = cards.Where(c => c.Kind == CardKinds.Question).Select(Ref);

        var contradictions = state.Connections.Values.Where(c => c.Kind == ConnectionKinds.Contradicts).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => new EdgeRef(c.Id, c.SourceId, TitleOf(c.SourceId), c.TargetId, TitleOf(c.TargetId), c.Label));

        var hubs = cards.Select(c => new Hub(Ref(c), neighbours[c.Id].Count)).Where(h => h.Degree >= 3)
            .OrderByDescending(h => h.Degree).ThenBy(h => h.Card.Id, StringComparer.Ordinal).Take(MaxHubs);

        var tagCounts = cards.SelectMany(c => tags[c.Id]).GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => new TagCount(g.Key, g.Count())).OrderByDescending(t => t.Count).ThenBy(t => t.Tag, StringComparer.Ordinal);

        return new(canvasId, version, state.Cards.Count, state.Regions.Count, state.Connections.Count, textLinks, Gaps(),
            [.. orphans.Take(MaxListed)], [.. dangling.Take(MaxListed)], [.. questions.Take(MaxListed)], [.. contradictions.Take(MaxListed)], [.. hubs], [.. tagCounts.Take(MaxListed)]);
    }

    /// <summary>
    /// Pairs of regions whose cards share vocabulary but have no connection or text link between them (InfraNodus's
    /// "structural gaps"): the places where a bridging idea or question is most likely missing.
    /// </summary>
    public IReadOnlyList<RegionGap> Gaps()
    {
        var regionVectors = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (var card in state.Cards.Values)
        {
            if (card.RegionId is null) continue;
            var vector = regionVectors.TryGetValue(card.RegionId, out var v) ? v : regionVectors[card.RegionId] = new(StringComparer.Ordinal);
            foreach (var (term, weight) in vectors[card.Id]) vector[term] = vector.GetValueOrDefault(term) + weight;
        }

        var bridged = new HashSet<(string, string)>();
        void Bridge(string? a, string? b)
        {
            foreach (var x in WithParent(a))
                foreach (var y in WithParent(b))
                    if (x != y) bridged.Add(Pair(x, y));
        }
        foreach (var connection in state.Connections.Values) Bridge(RegionOf(connection.SourceId), RegionOf(connection.TargetId));
        foreach (var card in state.Cards.Values)
            foreach (var target in links.Outgoing(card.Id).Outgoing) Bridge(card.RegionId, target.RegionId);

        var ids = regionVectors.Keys.Where(id => regionVectors[id].Count > 0).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var gaps = new List<RegionGap>();
        for (var i = 0; i < ids.Count; i++)
            for (var j = i + 1; j < ids.Count; j++)
            {
                var (a, b) = (state.Regions[ids[i]], state.Regions[ids[j]]);
                if (a.ParentId == b.Id || b.ParentId == a.Id || bridged.Contains(Pair(a.Id, b.Id))) continue;
                var (va, vb) = (regionVectors[a.Id], regionVectors[b.Id]);
                var shared = va.Keys.Where(vb.ContainsKey).OrderByDescending(t => Math.Min(va[t], vb[t])).ThenBy(t => t, StringComparer.Ordinal).ToList();
                var similarity = Cosine(va, vb);
                if (shared.Count < 2 || similarity < 0.08) continue;
                gaps.Add(new(a.Id, a.Title, b.Id, b.Title, Math.Round(similarity, 3), [.. shared.Take(6)]));
            }
        return [.. gaps.OrderByDescending(g => g.Similarity).ThenBy(g => g.FirstId, StringComparer.Ordinal).Take(MaxGaps)];
    }

    private string? RegionOf(string id) => EntityIds.KindOf(id) switch
    {
        EntityKind.Card => state.Cards.GetValueOrDefault(id)?.RegionId,
        EntityKind.Region => state.Regions.ContainsKey(id) ? id : null,
        _ => null
    };

    private IEnumerable<string> WithParent(string? regionId)
    {
        if (regionId is null || !state.Regions.TryGetValue(regionId, out var region)) yield break;
        yield return region.Id;
        if (region.ParentId is not null) yield return region.ParentId;
    }

    private string TitleOf(string id) => state.Find(id) switch
    {
        Card card => card.Title,
        Region region => region.Title,
        _ => id
    };

    private static (string, string) Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);

    private static double Cosine(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        double dot = 0;
        foreach (var (term, weight) in a)
            if (b.TryGetValue(term, out var other)) dot += weight * other;
        if (dot == 0) return 0;
        return dot / (Math.Sqrt(a.Values.Sum(w => w * w)) * Math.Sqrt(b.Values.Sum(w => w * w)));
    }
}
