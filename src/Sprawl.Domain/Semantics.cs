using System.Text.RegularExpressions;
using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// Meaning derived from card text, never stored separately (so it can never drift from the text):
/// <c>[[Card title]]</c> or <c>[[cd_…]]</c> links, <c>#tags</c>, unlinked mentions and key terms for similarity.
/// Code (fenced blocks and inline spans) is ignored, so <c>#include</c> or <c>a[[0]]</c> in code mean nothing.
/// </summary>
public static partial class Semantics
{
    public const int MaxTagsPerCard = 12;

    /// <summary>Card text with code removed.</summary>
    public static string Prose(string text) => InlineCode().Replace(FencedCode().Replace(text, " "), " ");

    /// <summary>The raw targets of <c>[[…]]</c> links, in order, without duplicates.</summary>
    public static IReadOnlyList<string> LinkTargets(string text) =>
        [.. WikiLink().Matches(Prose(text)).Select(m => m.Groups[1].Value.Split('|')[0].Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Lower-case tags from <c>#tag</c> in the title and body (not markdown headings, URLs or code).</summary>
    public static IReadOnlyList<string> Tags(Card card) =>
        [.. Hashtag().Matches(Prose(card.Title + "\n" + card.Body)).Select(m => m.Groups[1].Value.ToLowerInvariant().TrimEnd('-'))
            .Where(t => t.Length > 0 && !t.All(char.IsDigit)).Distinct(StringComparer.Ordinal).Take(MaxTagsPerCard)];

    /// <summary>Resolve a link target on this canvas: an id, or a title (case- and punctuation-insensitive; oldest first).</summary>
    public static Card? Resolve(CanvasState state, string target) => LinkGraph.Build(state).Resolve(target);

    public sealed record Links(IReadOnlyList<Card> Outgoing, IReadOnlyList<string> Unresolved);

    /// <summary>Every text link on a canvas, resolved once: outgoing and incoming per card, and targets that name no card.</summary>
    public sealed class LinkGraph
    {
        private readonly CanvasState state;
        private readonly Dictionary<string, Card> byTitle = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Links> outgoing = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Card>> incoming = new(StringComparer.Ordinal);

        private LinkGraph(CanvasState state)
        {
            this.state = state;
            foreach (var card in state.Cards.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
                byTitle.TryAdd(Text.Normalize(card.Title), card);
            foreach (var card in state.Cards.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
            {
                var resolved = new List<Card>();
                var unresolved = new List<string>();
                foreach (var target in LinkTargets(card.Body))
                {
                    var found = Resolve(target);
                    if (found is null) unresolved.Add(target);
                    else if (found.Id != card.Id && resolved.All(c => c.Id != found.Id))
                    {
                        resolved.Add(found);
                        (incoming.TryGetValue(found.Id, out var list) ? list : incoming[found.Id] = []).Add(card);
                    }
                }
                outgoing[card.Id] = new(resolved, unresolved);
            }
        }

        public static LinkGraph Build(CanvasState state) => new(state);

        public Card? Resolve(string target) =>
            state.Cards.TryGetValue(target.Trim(), out var byId) ? byId : byTitle.GetValueOrDefault(Text.Normalize(target));

        public Links Outgoing(string cardId) => outgoing.GetValueOrDefault(cardId) ?? new([], []);

        public IReadOnlyList<Card> Backlinks(string cardId) => incoming.GetValueOrDefault(cardId) ?? [];

        /// <summary>True when either card links to the other in text.</summary>
        public bool Linked(string a, string b) => Outgoing(a).Outgoing.Any(c => c.Id == b) || Outgoing(b).Outgoing.Any(c => c.Id == a);
    }

    public static Links OutgoingLinks(CanvasState state, Card card) => LinkGraph.Build(state).Outgoing(card.Id);

    /// <summary>Cards whose text links to <paramref name="card"/>.</summary>
    public static IReadOnlyList<Card> Backlinks(CanvasState state, Card card) => LinkGraph.Build(state).Backlinks(card.Id);

    /// <summary>
    /// Cards that mention <paramref name="card"/>'s title as a phrase without linking or connecting to it: candidates to
    /// link (TheBrain's "unlinked mentions"). Titles shorter than four characters are too ambiguous to look for.
    /// </summary>
    public static IReadOnlyList<Card> UnlinkedMentions(CanvasState state, Card card, LinkGraph? graph = null)
    {
        graph ??= LinkGraph.Build(state);
        var title = Text.Normalize(card.Title);
        if (title.Length < 4) return [];
        var phrase = new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(title) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        var connected = state.Connections.Values.Where(c => c.SourceId == card.Id || c.TargetId == card.Id)
            .Select(c => c.SourceId == card.Id ? c.TargetId : c.SourceId).ToHashSet(StringComparer.Ordinal);
        return [.. state.Cards.Values
            .Where(other => other.Id != card.Id && !connected.Contains(other.Id) && !Text.SameTitle(other.Title, card.Title))
            .Where(other => phrase.IsMatch(Prose(other.Title + "\n" + other.Body)))
            .Where(other => !graph.Linked(other.Id, card.Id))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal)];
    }

    /// <summary>Turn the first plain mention of <paramref name="title"/> in <paramref name="body"/> into a <c>[[link]]</c>.</summary>
    public static string LinkFirstMention(string body, string title)
    {
        var phrase = new Regex(@"(?<![\p{L}\p{N}\[])" + Regex.Escape(title.Trim()) + @"(?![\p{L}\p{N}\]])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        var match = phrase.Match(body);
        return match.Success ? body[..match.Index] + "[[" + match.Value + "]]" + body[(match.Index + match.Length)..] : body.TrimEnd() + (body.Length > 0 ? " " : "") + "[[" + title.Trim() + "]]";
    }

    /// <summary>The words that best characterise a text, for similarity search: frequent, long-ish, not stop words.</summary>
    public static IReadOnlyList<string> KeyTerms(string title, string body, int max = 8)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string text, int weight)
        {
            foreach (Match m in Word().Matches(Prose(text).ToLowerInvariant()))
            {
                var w = m.Value;
                if (w.Length < 4 || StopWords.Contains(w) || w.All(char.IsDigit)) continue;
                counts[w] = counts.GetValueOrDefault(w) + weight;
            }
        }
        Add(title, 3);
        Add(body, 1);
        return [.. counts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key.Length).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(max).Select(kv => kv.Key)];
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "about", "above", "after", "again", "against", "also", "because", "been", "before", "being", "below", "between", "both", "but", "cannot",
        "could", "does", "doing", "down", "during", "each", "even", "every", "from", "further", "have", "having", "here", "into", "itself", "just",
        "like", "more", "most", "much", "must", "only", "other", "over", "same", "should", "since", "some", "still", "such", "than", "that", "their",
        "them", "then", "there", "these", "they", "this", "those", "through", "under", "until", "very", "what", "when", "where", "which", "while",
        "will", "with", "would", "your", "into", "onto", "upon", "within", "without", "make", "makes", "made", "using", "used", "uses", "thing", "things",
        "many", "first", "second", "another", "across", "among", "always", "never", "often", "really", "where", "whose", "https", "http", "www"
    };

    [GeneratedRegex(@"```[\s\S]*?(```|$)")] private static partial Regex FencedCode();
    [GeneratedRegex(@"`[^`\n]*`")] private static partial Regex InlineCode();
    [GeneratedRegex(@"\[\[([^\[\]\n]{1,160})\]\]")] private static partial Regex WikiLink();
    [GeneratedRegex(@"(?<![\p{L}\p{N}_/#&=?:.])#([\p{L}\p{N}][\p{L}\p{N}_-]{0,31})")] private static partial Regex Hashtag();
    [GeneratedRegex(@"[\p{L}][\p{L}\p{N}'-]*")] private static partial Regex Word();
}
