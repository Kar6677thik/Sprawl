using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// Feedback loops in a causal-loop diagram (a region with the <c>loops</c> layout), as Kumu and system dynamics draw
/// them. A connection's label carries its polarity: "+" (same direction: more causes more) or "−"/"-" (opposite: more
/// causes less); "||" or the word "delay" in the label marks a delay. A loop with an even number of − links is
/// reinforcing (R), with an odd number balancing (B); a loop with an unsigned link has no polarity. Loops are named R1,
/// R2, B1, … in a fixed order (longest first, then by their members), so names stay stable while the diagram does.
/// </summary>
public static class CausalLoops
{
    public const int MaxLoops = 24;
    public const int MaxLength = 12;

    public sealed record Loop(string Name, char? Polarity, IReadOnlyList<string> Members, IReadOnlyList<string> Connections, bool Delayed);

    /// <summary>+1, −1, or 0 when the label carries no sign.</summary>
    public static int Sign(string? label)
    {
        var text = (label ?? "").Trim();
        if (text.Length == 0) return 0;
        return text[0] switch
        {
            '+' => 1,
            '-' or '−' or '–' => -1,
            _ => text.Equals("s", StringComparison.OrdinalIgnoreCase) ? 1 : text.Equals("o", StringComparison.OrdinalIgnoreCase) ? -1 : 0
        };
    }

    public static bool IsDelay(string? label) =>
        label is not null && (label.Contains("||", StringComparison.Ordinal) || label.Contains("delay", StringComparison.OrdinalIgnoreCase));

    /// <summary>The elementary cycles among a region's direct members (cards and sub-regions), at most <see cref="MaxLoops"/>.</summary>
    public static IReadOnlyList<Loop> Find(CanvasState state, string regionId)
    {
        var members = state.CardsIn(regionId).Select(c => (c.Id, c.CreatedAt))
            .Concat(state.RegionsIn(regionId).Select(r => (r.Id, r.CreatedAt)))
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id, StringComparer.Ordinal).Select(m => m.Id).ToList();
        var index = members.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i, StringComparer.Ordinal);
        var outgoing = members.Select(_ => new List<(int To, Connection Via)>()).ToList();
        foreach (var c in state.Connections.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            if (!index.TryGetValue(c.SourceId, out var a) || !index.TryGetValue(c.TargetId, out var b) || a == b) continue;
            var ends = c.Ends ?? (ConnectionStyle.Directional(c.Kind) ? EndStyles.End : EndStyles.None);
            if (ends is EndStyles.End or EndStyles.Both or EndStyles.None) outgoing[a].Add((b, c));
            if (ends is EndStyles.Start or EndStyles.Both) outgoing[b].Add((a, c));
        }

        // Each cycle once: started from its lowest member, visiting only higher ones (the core of Johnson's algorithm,
        // without its blocking lists, which a diagram of tens of nodes and a cap on length and count does not need).
        var found = new List<(List<int> Nodes, List<Connection> Via)>();
        var path = new List<int>();
        var via = new List<Connection>();
        var onPath = new bool[members.Count];
        var budget = 200_000;
        void Walk(int start, int at)
        {
            foreach (var (to, connection) in outgoing[at])
            {
                if (found.Count >= MaxLoops * 4 || --budget < 0) return;
                if (to == start) { found.Add(([.. path], [.. via, connection])); continue; }
                if (to < start || onPath[to] || path.Count >= MaxLength) continue;
                path.Add(to); via.Add(connection); onPath[to] = true;
                Walk(start, to);
                path.RemoveAt(path.Count - 1); via.RemoveAt(via.Count - 1); onPath[to] = false;
            }
        }
        for (var start = 0; start < members.Count; start++)
        {
            path.Clear(); via.Clear(); path.Add(start); onPath[start] = true;
            Walk(start, start);
            onPath[start] = false;
        }

        var loops = found
            .OrderByDescending(l => l.Nodes.Count).ThenBy(l => string.Join(',', l.Nodes.Select(n => n.ToString("D4", System.Globalization.CultureInfo.InvariantCulture))), StringComparer.Ordinal)
            .Take(MaxLoops).ToList();
        var counters = new Dictionary<char, int> { ['R'] = 0, ['B'] = 0, ['L'] = 0 };
        var result = new List<Loop>();
        foreach (var (nodes, links) in loops)
        {
            var signs = links.Select(c => Sign(c.Label)).ToList();
            char? polarity = signs.Contains(0) ? null : signs.Count(s => s < 0) % 2 == 0 ? 'R' : 'B';
            var letter = polarity ?? 'L';
            counters[letter]++;
            result.Add(new Loop($"{letter}{counters[letter]}", polarity, [.. nodes.Select(n => members[n])], [.. links.Select(c => c.Id)], links.Any(c => IsDelay(c.Label))));
        }
        return result;
    }

    /// <summary>The order to place members around the circle: each loop's members in turn, longest loop first, then the rest.</summary>
    public static IReadOnlyList<string> CircleOrder(CanvasState state, string regionId, IReadOnlyList<string> members)
    {
        var order = new List<string>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var loop in Find(state, regionId))
        {
            // A loop that shares a member with one already placed is inserted right after it, so it stays beside it.
            var shared = loop.Members.Select((m, i) => (m, i)).Where(p => placed.Contains(p.m)).Select(p => p.i).DefaultIfEmpty(-1).First();
            var start = Math.Max(0, shared);
            var at = shared < 0 ? order.Count : order.IndexOf(loop.Members[shared]) + 1;
            for (var k = shared < 0 ? 0 : 1; k < loop.Members.Count; k++)
            {
                var id = loop.Members[(start + k) % loop.Members.Count];
                if (placed.Contains(id)) { at = order.IndexOf(id) + 1; continue; }
                if (!members.Contains(id)) continue;
                placed.Add(id);
                order.Insert(at++, id);
            }
        }
        foreach (var id in members) if (placed.Add(id)) order.Add(id);
        return order;
    }
}
