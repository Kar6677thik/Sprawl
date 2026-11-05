using System.Text;
using System.Text.RegularExpressions;
using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// Mermaid flowcharts in and out. Claude and other models write Mermaid fluently, so a whole architecture or flow
/// arrives as one <c>addDiagram</c> intent and is laid out by the server; <see cref="Export"/> gives it back.
/// Supported: <c>flowchart|graph TD|TB|BT|LR|RL</c>, node shapes (<c>[ ] ( ) ([ ]) [[ ]] [( )] (( )) { } {{ }} [/ /] [\ \] &gt; ]</c>
/// and <c>id@{ shape: …, label: … }</c>), chains (<c>A --&gt; B --&gt; C</c>), <c>&amp;</c>, every arrow form (<c>--&gt; --- -.-&gt; ==&gt; &lt;--&gt;
/// --o --x</c>, with any number of dashes), edge labels (<c>--&gt;|yes|</c>, <c>-- yes --&gt;</c>), <c>subgraph</c> (one level; deeper
/// ones fold into their outer subgraph) with its own <c>direction</c>. Styling statements are ignored.
/// </summary>
public static partial class Mermaid
{
    public sealed record Node(string Id, string Label, string Shape, string? Subgraph, string? Icon = null);
    public sealed record Edge(string From, string To, string? Label, string Line, string Ends);
    public sealed record Subgraph(string Id, string Title, string? Layout);
    public sealed record Chart(string Layout, IReadOnlyList<Node> Nodes, IReadOnlyList<Edge> Edges, IReadOnlyList<Subgraph> Subgraphs);

    public const string Flowchart = "flowchart";

    /// <summary>The diagram types Mermaid renders, by their opening keyword. Flowcharts become native shapes; the rest are rendered cards.</summary>
    public static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "flowchart", "graph", "sequenceDiagram", "classDiagram", "classDiagram-v2", "stateDiagram", "stateDiagram-v2", "erDiagram", "journey", "gantt", "pie",
        "quadrantChart", "requirementDiagram", "gitGraph", "C4Context", "C4Container", "C4Component", "C4Dynamic", "C4Deployment", "mindmap", "timeline",
        "sankey", "sankey-beta", "xychart", "xychart-beta", "block", "block-beta", "packet", "packet-beta", "kanban", "architecture", "architecture-beta",
        "radar", "radar-beta", "treemap", "treemap-beta", "venn", "venn-beta", "wardley", "wardley-beta", "zenuml"
    };

    /// <summary>The source's diagram type (<see cref="Flowchart"/> for flowcharts and graphs), skipping front matter and comments.</summary>
    public static string DiagramType(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw SprawlException.Invalid("The Mermaid text is empty.");
        if (source.Length > Limits.Mermaid) throw SprawlException.Limit($"A Mermaid diagram is at most {Limits.Mermaid} characters.");
        var lines = source.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).ToList();
        var i = 0;
        if (i < lines.Count && lines[i] == "---") { i++; while (i < lines.Count && lines[i] != "---") i++; i++; }
        for (; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal)) continue;
            var keyword = line.Split((char[])[' ', '\t', ':', '{'], 2)[0];
            if (!Types.Contains(keyword)) break;
            return keyword is "flowchart" or "graph" ? Flowchart : keyword;
        }
        throw SprawlException.Invalid("Start the diagram with a Mermaid type: flowchart TD/LR, sequenceDiagram, classDiagram, stateDiagram-v2, erDiagram, mindmap, timeline, gantt, journey, quadrantChart, sankey-beta, xychart-beta, architecture-beta, C4Context, gitGraph, pie, kanban, block-beta, ….");
    }

    public static Chart Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw SprawlException.Invalid("The Mermaid text is empty.");
        if (source.Length > Limits.Mermaid) throw SprawlException.Limit($"A Mermaid diagram is at most {Limits.Mermaid} characters.");
        var statements = Statements(source).ToList();
        if (statements.Count == 0) throw SprawlException.Invalid("The Mermaid text is empty.");
        var header = Header().Match(statements[0]);
        if (!header.Success) throw SprawlException.Invalid("Start the diagram with 'flowchart TD' (or LR, BT, RL; 'graph' works too). Only flowcharts are supported.");
        var layout = DirectionLayout(header.Groups[2].Value) ?? RegionLayouts.FlowDown;

        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var order = new List<string>();
        var edges = new List<Edge>();
        var subgraphs = new List<Subgraph>();
        var stack = new Stack<string>();   // open subgraphs; only the outermost one becomes a sub-region

        void Declare(string id, string? label, string? shape, string? icon)
        {
            var subgraph = stack.Count == 0 ? null : stack.Last();
            if (nodes.TryGetValue(id, out var existing))
            {
                nodes[id] = existing with
                {
                    Label = label ?? existing.Label,
                    Shape = shape ?? existing.Shape,
                    Icon = icon ?? existing.Icon,
                    Subgraph = existing.Subgraph ?? subgraph
                };
                return;
            }
            if (subgraphs.Any(s => s.Id == id) && label is null && shape is null) return; // an edge to a subgraph
            if (nodes.Count >= Limits.DiagramNodes) throw SprawlException.Limit($"A diagram holds at most {Limits.DiagramNodes} nodes.");
            nodes[id] = new Node(id, label ?? id, shape ?? Shapes.Rectangle, subgraph, icon);
            order.Add(id);
        }

        foreach (var statement in statements.Skip(1))
        {
            if (Ignored().IsMatch(statement)) continue;
            if (SubgraphStart().Match(statement) is { Success: true } open)
            {
                var (id, title) = SubgraphName(open.Groups[1].Value.Trim());
                if (stack.Count == 0 && subgraphs.All(s => s.Id != id)) subgraphs.Add(new Subgraph(id, title, null));
                stack.Push(stack.Count == 0 ? id : stack.Peek());
                continue;
            }
            if (statement == "end")
            {
                if (stack.Count > 0) stack.Pop();
                continue;
            }
            if (DirectionStatement().Match(statement) is { Success: true } direction)
            {
                if (stack.Count > 0)
                {
                    var target = stack.Last();
                    var index = subgraphs.FindIndex(s => s.Id == target);
                    if (index >= 0) subgraphs[index] = subgraphs[index] with { Layout = DirectionLayout(direction.Groups[1].Value) };
                }
                continue;
            }
            ParseChain(statement, Declare, edges);
        }

        var subgraphIds = subgraphs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in edges)
            foreach (var end in new[] { edge.From, edge.To })
                if (!nodes.ContainsKey(end) && !subgraphIds.Contains(end)) Declare(end, null, null, null);
        return new Chart(layout, [.. order.Select(id => nodes[id])], edges, subgraphs);
    }

    /// <summary>One statement: node groups joined by edges, e.g. <c>A[Start] --&gt;|go| B &amp; C --&gt; D</c>.</summary>
    private static void ParseChain(string text, Action<string, string?, string?, string?> declare, List<Edge> edges)
    {
        var position = 0;
        var previous = ReadGroup(text, ref position, declare);
        if (previous.Count == 0) throw SprawlException.Invalid($"Could not read the Mermaid line '{Clip(text)}'.");
        while (true)
        {
            SkipSpace(text, ref position);
            if (position >= text.Length) return;
            var link = ReadLink(text, ref position) ?? throw SprawlException.Invalid($"Expected an arrow in the Mermaid line '{Clip(text)}'.");
            var next = ReadGroup(text, ref position, declare);
            if (next.Count == 0) throw SprawlException.Invalid($"An arrow leads nowhere in the Mermaid line '{Clip(text)}'.");
            foreach (var from in previous)
                foreach (var to in next)
                    edges.Add(new Edge(from, to, link.Label, link.Line, link.Ends));
            previous = next;
        }
    }

    private static List<string> ReadGroup(string text, ref int position, Action<string, string?, string?, string?> declare)
    {
        var ids = new List<string>();
        while (true)
        {
            SkipSpace(text, ref position);
            var id = NodeId().Match(text, position);
            if (!id.Success || id.Index != position) return ids;
            position += id.Length;
            var (label, shape, icon) = ReadShape(text, ref position);
            declare(id.Value, label, shape, icon);
            ids.Add(id.Value);
            SkipSpace(text, ref position);
            if (position < text.Length && text[position] == '&') { position++; continue; }
            return ids;
        }
    }

    private static readonly (string Open, string[] Close, string Shape)[] Brackets =
    [
        ("(((", [")))"], Shapes.Circle), ("((", ["))"], Shapes.Circle), ("([", ["])"], Shapes.Terminal), ("[[", ["]]"], Shapes.Subroutine),
        ("[(", [")]"], Shapes.Cylinder), ("{{", ["}}"], Shapes.Hexagon), ("[/", ["/]", "\\]"], Shapes.Parallelogram),
        ("[\\", ["\\]", "/]"], Shapes.Parallelogram), ("(", [")"], Shapes.Rounded), ("[", ["]"], Shapes.Rectangle),
        ("{", ["}"], Shapes.Diamond), (">", ["]"], Shapes.Rectangle)
    ];

    private static (string? Label, string? Shape, string? Icon) ReadShape(string text, ref int position)
    {
        if (position < text.Length - 1 && text[position] == '@' && text[position + 1] == '{')
        {
            var close = text.IndexOf('}', position);
            if (close < 0) throw SprawlException.Invalid("An '@{' node is not closed with '}'.");
            var body = text[(position + 2)..close];
            position = close + 1;
            var shape = Property().Matches(body).FirstOrDefault(m => m.Groups[1].Value == "shape")?.Groups[2].Value;
            var label = Property().Matches(body).FirstOrDefault(m => m.Groups[1].Value == "label")?.Groups[2].Value;
            var icon = Property().Matches(body).FirstOrDefault(m => m.Groups[1].Value == "icon")?.Groups[2].Value;
            var known = icon is null ? null : IconNames.Normalize(icon.Trim('"', '\'', ' '));
            return (label is null ? null : CleanLabel(label), shape is null ? null : NamedShape(shape.Trim('"', '\'', ' ')),
                known is not null && Icons.All.Contains(known) ? known : null);
        }
        foreach (var (open, closers, shape) in Brackets)
        {
            if (string.CompareOrdinal(text, position, open, 0, open.Length) != 0) continue;
            var start = position + open.Length;
            int end = -1, closeLength = 0;
            if (start < text.Length && text[start] == '"')
            {
                var quote = text.IndexOf('"', start + 1);
                if (quote < 0) throw SprawlException.Invalid("A quoted Mermaid label is not closed.");
                foreach (var closer in closers)
                    if (string.CompareOrdinal(text, quote + 1, closer, 0, closer.Length) == 0) { end = quote + 1; closeLength = closer.Length; break; }
            }
            else
                foreach (var closer in closers)
                {
                    var at = text.IndexOf(closer, start, StringComparison.Ordinal);
                    if (at >= 0 && (end < 0 || at < end)) { end = at; closeLength = closer.Length; }
                }
            if (end < 0) throw SprawlException.Invalid($"A Mermaid node shape '{open}' is not closed.");
            var label = CleanLabel(text[start..end]);
            position = end + closeLength;
            return (label, shape, null);
        }
        return (null, null, null);
    }

    private sealed record Link(string? Label, string Line, string Ends);

    private static Link? ReadLink(string text, ref int position)
    {
        var match = LabelledLink().Match(text, position);
        if (!match.Success || match.Index != position) match = PlainLink().Match(text, position);
        if (!match.Success || match.Index != position) return null;
        position += match.Length;
        var body = match.Groups["body"].Value + match.Groups["tail"].Value;
        var line = body.Contains('.') ? LineStyles.Dashed : body.Contains('=') ? LineStyles.Thick : LineStyles.Solid;
        var left = match.Groups["left"].Value.Length > 0;
        var right = match.Groups["right"].Value.Length > 0;
        var ends = left && right ? EndStyles.Both : right ? EndStyles.End : left ? EndStyles.Start : EndStyles.None;
        string? label = match.Groups["text"].Success && match.Groups["text"].Value.Trim().Length > 0 ? CleanLabel(match.Groups["text"].Value) : null;
        SkipSpace(text, ref position);
        if (position < text.Length && text[position] == '|')
        {
            var close = text.IndexOf('|', position + 1);
            if (close < 0) throw SprawlException.Invalid("An edge label '|…' is not closed with '|'.");
            label = CleanLabel(text[(position + 1)..close]);
            position = close + 1;
        }
        return new Link(string.IsNullOrEmpty(label) ? null : label, line, ends);
    }

    /// <summary>A diagram region (and its sub-regions) as a Mermaid flowchart: shapes, labels, connections and styles.</summary>
    public static string Export(CanvasState state, Region region)
    {
        // A region that is one rendered Mermaid diagram reads back as its own source.
        var only = state.CardsIn(region.Id).ToList();
        if (only.Count == 1 && only[0].Kind == CardKinds.Mermaid && !state.RegionsIn(region.Id).Any()) return only[0].Body.TrimEnd() + "\n";
        var direction = region.Layout == RegionLayouts.FlowRight ? "LR" : "TD";
        var md = new StringBuilder($"flowchart {direction}\n");
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        string Id(string entityId) => ids.TryGetValue(entityId, out var id) ? id : ids[entityId] = "n" + (ids.Count + 1);
        void Nodes(string regionId, string indent)
        {
            foreach (var card in OutlineRenderer.ReadingOrder(state.CardsIn(regionId)))
                md.Append(indent).Append(Id(card.Id)).Append(Wrap(card.Shape ?? Shapes.Rounded, card.Title, card.Icon)).Append('\n');
        }
        Nodes(region.Id, "  ");
        foreach (var sub in OutlineRenderer.ReadingOrder(state.RegionsIn(region.Id)))
        {
            md.Append("  subgraph ").Append(Id(sub.Id)).Append(" [\"").Append(Escape(sub.Title)).Append("\"]\n");
            if (RegionLayouts.IsDiagram(sub.Layout) && sub.Layout != region.Layout) md.Append("    direction ").Append(sub.Layout == RegionLayouts.FlowRight ? "LR" : "TB").Append('\n');
            Nodes(sub.Id, "    ");
            md.Append("  end\n");
        }
        foreach (var c in state.Connections.Values.Where(c => ids.ContainsKey(c.SourceId) && ids.ContainsKey(c.TargetId))
                     .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            var ends = c.Ends ?? (ConnectionStyle.Directional(c.Kind) ? EndStyles.End : EndStyles.None);
            var (from, to) = (c.SourceId, c.TargetId);
            if (ends == EndStyles.Start) (from, to, ends) = (to, from, EndStyles.End); // Mermaid has no start-only arrow
            var arrow = ((c.Line ?? LineStyles.Solid), ends) switch
            {
                (LineStyles.Dashed or LineStyles.Dotted, EndStyles.None) => "-.-",
                (LineStyles.Dashed or LineStyles.Dotted, EndStyles.End) => "-.->",
                (LineStyles.Dashed or LineStyles.Dotted, _) => "<-.->",
                (LineStyles.Thick, EndStyles.None) => "===",
                (LineStyles.Thick, EndStyles.End) => "==>",
                (LineStyles.Thick, _) => "<==>",
                (_, EndStyles.None) => "---",
                (_, EndStyles.End) => "-->",
                _ => "<-->"
            };
            var label = c.Label ?? (c.Kind is ConnectionKinds.Relates or ConnectionKinds.LeadsTo ? null : c.Kind);
            md.Append("  ").Append(Id(from)).Append(' ').Append(arrow).Append(label is null ? "" : $"|\"{Escape(label)}\"|").Append(' ').Append(Id(to)).Append('\n');
        }
        return md.ToString();
    }

    private static string Wrap(string shape, string title, string? icon = null)
    {
        var t = "\"" + Escape(title) + "\"";
        if (icon is not null) return $"@{{ shape: {MermaidShape(shape)}, label: {t}, icon: \"{icon}\" }}";
        return shape switch
        {
            Shapes.Rectangle or Shapes.Text => $"[{t}]",
            Shapes.Terminal => $"([{t}])",
            Shapes.Subroutine => $"[[{t}]]",
            Shapes.Cylinder => $"[({t})]",
            Shapes.Circle => $"(({t}))",
            Shapes.Diamond => $"{{{t}}}",
            Shapes.Hexagon => $"{{{{{t}}}}}",
            Shapes.Parallelogram => $"[/{t}/]",
            Shapes.Document => $"@{{ shape: doc, label: {t} }}",
            Shapes.Cloud => $"@{{ shape: cloud, label: {t} }}",
            Shapes.Person => $"@{{ shape: stadium, label: {t} }}",
            Shapes.Queue => $"@{{ shape: h-cyl, label: {t} }}",
            Shapes.Ellipse => $"(({t}))",
            _ => $"({t})"
        };
    }

    private static string MermaidShape(string shape) => shape switch
    {
        Shapes.Rectangle or Shapes.Text => "rect", Shapes.Terminal or Shapes.Person => "stadium", Shapes.Subroutine => "subproc", Shapes.Cylinder => "cyl",
        Shapes.Circle or Shapes.Ellipse => "circle", Shapes.Diamond => "diam", Shapes.Hexagon => "hex", Shapes.Parallelogram => "lean-r",
        Shapes.Document => "doc", Shapes.Cloud => "cloud", Shapes.Queue => "h-cyl", _ => "rounded"
    };

    private static string Escape(string text) => text.Replace("\"", "#quot;", StringComparison.Ordinal);

    private static string? NamedShape(string name) => name.ToLowerInvariant() switch
    {
        "rect" or "proc" or "process" or "rectangle" => Shapes.Rectangle,
        "rounded" or "event" => Shapes.Rounded,
        "stadium" or "pill" or "terminal" => Shapes.Terminal,
        "diam" or "diamond" or "decision" or "question" => Shapes.Diamond,
        "circle" or "circ" or "dbl-circ" or "double-circle" or "sm-circ" or "start" or "stop" or "fr-circ" => Shapes.Circle,
        "cyl" or "cylinder" or "db" or "database" or "das" or "lin-cyl" or "disk" => Shapes.Cylinder,
        "h-cyl" or "horizontal-cylinder" or "queue" => Shapes.Queue,
        "lean-r" or "lean-l" or "in-out" or "out-in" or "trap-t" or "trap-b" or "manual-input" or "priority" => Shapes.Parallelogram,
        "hex" or "hexagon" or "prepare" => Shapes.Hexagon,
        "doc" or "document" or "docs" or "documents" or "lin-doc" or "tag-doc" => Shapes.Document,
        "cloud" => Shapes.Cloud,
        "subproc" or "subprocess" or "subroutine" or "fr-rect" or "framed-rectangle" => Shapes.Subroutine,
        "text" => Shapes.Text,
        "ellipse" or "oval" => Shapes.Ellipse,
        "person" or "actor" or "user" => Shapes.Person,
        _ => Shapes.Rectangle
    };

    private static string? DirectionLayout(string direction) => direction.ToUpperInvariant() switch
    {
        "LR" or "RL" => RegionLayouts.FlowRight,
        "TD" or "TB" or "BT" => RegionLayouts.FlowDown,
        _ => null
    };

    private static (string Id, string Title) SubgraphName(string text)
    {
        var bracket = text.IndexOf('[');
        if (bracket > 0 && text.EndsWith(']')) return (text[..bracket].Trim(), CleanLabel(text[(bracket + 1)..^1]));
        var clean = CleanLabel(text);
        return (text.Contains(' ') || text.StartsWith('"') ? clean : text, clean);
    }

    private static string CleanLabel(string raw)
    {
        var label = raw.Trim();
        if (label.Length >= 2 && label[0] == '"' && label[^1] == '"') label = label[1..^1];
        if (label.Length >= 2 && label[0] == '`' && label[^1] == '`') label = label[1..^1];
        label = BreakTag().Replace(label, " ").Replace("#quot;", "\"", StringComparison.Ordinal).Replace("#amp;", "&", StringComparison.Ordinal);
        return Spaces().Replace(label, " ").Trim();
    }

    private static IEnumerable<string> Statements(string source)
    {
        foreach (var raw in source.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw;
            var comment = line.IndexOf("%%", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];
            foreach (var part in SplitSemicolons(line))
                if (part.Trim().Length > 0) yield return part.Trim();
        }
    }

    /// <summary>Split on ';' outside quotes and brackets, so a label may contain one.</summary>
    private static IEnumerable<string> SplitSemicolons(string line)
    {
        var depth = 0;
        var quoted = false;
        var start = 0;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') quoted = !quoted;
            else if (!quoted && c is '[' or '(' or '{') depth++;
            else if (!quoted && c is ']' or ')' or '}') depth = Math.Max(0, depth - 1);
            else if (!quoted && depth == 0 && c == ';') { yield return line[start..i]; start = i + 1; }
        }
        yield return line[start..];
    }

    private static void SkipSpace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
    }

    private static string Clip(string text) => text.Length <= 60 ? text : text[..57] + "...";

    [GeneratedRegex(@"^(flowchart|graph)(?:\s+(TD|TB|BT|LR|RL))?\s*$", RegexOptions.IgnoreCase)] private static partial Regex Header();
    [GeneratedRegex(@"^(classDef|class|style|linkStyle|click|accTitle|accDescr|title)\b", RegexOptions.IgnoreCase)] private static partial Regex Ignored();
    [GeneratedRegex(@"^subgraph\s+(.+)$")] private static partial Regex SubgraphStart();
    [GeneratedRegex(@"^direction\s+(TD|TB|BT|LR|RL)$", RegexOptions.IgnoreCase)] private static partial Regex DirectionStatement();
    [GeneratedRegex(@"[\p{L}\p{N}_][\p{L}\p{N}_]*")] private static partial Regex NodeId();
    [GeneratedRegex(@"(\w+)\s*:\s*(""[^""]*""|'[^']*'|[^,}]+)")] private static partial Regex Property();
    // "-- text -->", "== text ==>", "-. text .->" (the text may not start with an arrow character).
    [GeneratedRegex(@"(?<left><)?(?<body>--|==|-\.)\s+(?<text>[^\s\->=.|][^|>]*?)\s+(?<tail>-{2,}|={2,}|\.+-)(?<right>>)?")] private static partial Regex LabelledLink();
    // "-->", "---", "-.->", "==>", "<-->", "--o", "x--x", any length.
    [GeneratedRegex(@"(?<left><|[ox](?=[-=]))?(?<body>-{2,}|={2,}|-\.+-)(?<right>>|[ox](?=\s|$|\|))?")] private static partial Regex PlainLink();
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex BreakTag();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}

/// <summary>Defaults shared by layout, outline and export.</summary>
public static class ConnectionStyle
{
    public static bool Directional(string kind) => kind is ConnectionKinds.LeadsTo or ConnectionKinds.DependsOn or ConnectionKinds.ExampleOf;
}
