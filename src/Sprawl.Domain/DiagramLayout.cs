using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>Sizes of diagram shapes, from the shape and its label (about 7 px per character at 13 px, wrapped).</summary>
public static class ShapeGeometry
{
    private const int CharWidth = 7;
    private const int LineHeight = 18;

    public const int IconRow = 28;

    public static bool ShowsIcon(string shape, string? icon) => icon is not null && shape is not (Shapes.Person or Shapes.Text);

    public static (int W, int H) Size(string shape, string title, string? icon = null)
    {
        var maxText = shape is Shapes.Circle ? 96 : shape is Shapes.Diamond ? 150 : 200;
        var oneLine = Math.Max(1, title.Length) * CharWidth;
        var lines = (int)Math.Ceiling(oneLine / (double)maxText);
        var textW = Math.Min(oneLine, maxText);
        var textH = lines * LineHeight;
        var (w, h) = shape switch
        {
            Shapes.Terminal => (textW + 56, Math.Max(48, textH + 22)),
            Shapes.Diamond => (Math.Max(144, (int)(textW * 1.5) + 40), Math.Max(88, (int)(textH * 1.6) + 44)),
            Shapes.Circle => (Math.Max(80, Math.Max(textW, textH) + 40), 0),
            Shapes.Ellipse => (textW + 64, Math.Max(64, textH + 36)),
            Shapes.Parallelogram or Shapes.Hexagon => (Math.Max(144, textW + 72), Math.Max(56, textH + 30)),
            Shapes.Cylinder => (Math.Max(112, textW + 40), Math.Max(84, textH + 52)),
            Shapes.Cloud => (Math.Max(144, textW + 80), Math.Max(80, textH + 50)),
            Shapes.Person => (Math.Max(88, textW + 16), 72 + textH),
            Shapes.Document or Shapes.Queue => (Math.Max(120, textW + 48), Math.Max(64, textH + 40)),
            Shapes.Text => (Math.Max(64, textW + 16), Math.Max(32, textH + 10)),
            _ => (Math.Max(120, textW + 40), Math.Max(56, textH + 30))
        };
        if (ShowsIcon(shape, icon)) h += shape == Shapes.Diamond ? IconRow + 12 : IconRow;
        if (shape == Shapes.Circle) h = w = Math.Max(w, h);
        return (Round(Math.Min(w, 320)), Round(h));
    }

    private static int Round(int value) => (value + 7) / 8 * 8;
}

/// <summary>
/// Lays out diagram regions as layered flows (a compact Sugiyama layout): cycles broken by reversing back edges,
/// longest-path ranks tightened towards successors, dummy nodes on long edges, barycenter sweeps to cut crossings, and
/// each node centred on its neighbours. Sub-regions are laid out first and then take part as single boxes. Items placed by
/// hand keep their place. Deterministic: the same diagram always lands the same way.
/// </summary>
public static class DiagramLayout
{
    public const int RankGap = 72;
    public const int NodeGap = 40;
    private const int DummySize = 16;

    private sealed class Item(string id, int main, int cross, bool dummy)
    {
        public string Id { get; } = id;
        public int Main { get; } = main;     // extent along the flow
        public int Cross { get; } = cross;   // extent across it
        public bool Dummy { get; } = dummy;
        public int Rank { get; set; }
        public double Position { get; set; } // cross-axis offset of the leading edge
        public List<Item> Up { get; } = [];
        public List<Item> Down { get; } = [];
    }

    /// <summary>Lay out every diagram region whose contents can move, sub-regions before their parents.</summary>
    public static void Apply(CanvasState state, LayoutEngine layout)
    {
        foreach (var region in state.Regions.Values.Where(r => RegionLayouts.IsDiagram(r.Layout))
                     .OrderBy(r => r.ParentId is null ? 1 : 0).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id, StringComparer.Ordinal).ToList())
        {
            Layout(state, layout, state.Regions[region.Id]);
            layout.Refit(region.Id);
        }
    }

    private static void Layout(CanvasState state, LayoutEngine layout, Region region)
    {
        var down = region.Layout != RegionLayouts.FlowRight;
        var members = new List<(string Id, int W, int H, DateTimeOffset At)>();
        foreach (var card in state.CardsIn(region.Id).Where(c => c.Placement == Placements.Auto)) members.Add((card.Id, card.W, card.H, card.CreatedAt));
        foreach (var sub in state.RegionsIn(region.Id).Where(r => r.Placement == Placements.Auto)) members.Add((sub.Id, sub.W, sub.H, sub.CreatedAt));
        if (members.Count == 0) return;
        members = [.. members.OrderBy(m => m.At).ThenBy(m => m.Id, StringComparer.Ordinal)];
        if (region.Layout == RegionLayouts.Loops) { Circle(state, layout, region, members); return; }

        var items = members.Select(m => new Item(m.Id, down ? m.H : m.W, down ? m.W : m.H, false)).ToList();
        var byId = items.ToDictionary(i => i.Id, StringComparer.Ordinal);

        // Edges between members; an endpoint inside a sub-region counts as that sub-region.
        string? Member(string id)
        {
            if (byId.ContainsKey(id)) return id;
            var regionId = EntityIds.KindOf(id) switch
            {
                EntityKind.Card => state.Cards.GetValueOrDefault(id)?.RegionId,
                EntityKind.Region => state.Regions.GetValueOrDefault(id)?.ParentId,
                _ => null
            };
            return regionId is not null && byId.ContainsKey(regionId) ? regionId : null;
        }
        var edges = new List<(Item From, Item To)>();
        var seen = new HashSet<(string, string)>();
        foreach (var c in state.Connections.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            if (Member(c.SourceId) is not { } a || Member(c.TargetId) is not { } b || a == b) continue;
            // A connection drawn with its arrow at the start reads the other way.
            var (from, to) = c.Ends == EndStyles.Start ? (b, a) : (a, b);
            if (seen.Add((from, to)) && !seen.Contains((to, from))) edges.Add((byId[from], byId[to]));
        }

        BreakCycles(items, edges);
        foreach (var (from, to) in edges) { from.Down.Add(to); to.Up.Add(from); }
        Rank(items);
        var layers = AddDummies(items);
        Order(layers);
        Place(layers);

        // Main-axis offsets: each layer as thick as its thickest item, items centred in it.
        var origin = (X: Geometry.Padding, Y: Geometry.Header);
        var minCross = layers.SelectMany(l => l).Min(i => i.Position);
        double offset = 0;
        foreach (var layer in layers)
        {
            var thickness = layer.Max(i => i.Main);
            foreach (var item in layer.Where(i => !i.Dummy))
            {
                var main = (int)Math.Round(offset + (thickness - item.Main) / 2.0);
                var cross = (int)Math.Round(item.Position - minCross);
                var (x, y) = down ? (origin.X + cross, origin.Y + main) : (origin.X + main, origin.Y + cross);
                Move(state, layout, item.Id, x, y);
            }
            offset += thickness + RankGap;
        }
        if (region.Cols != 1) state.Regions[region.Id] = state.Regions[region.Id] with { Cols = 1 };
    }

    public const int CircleGap = 64;

    /// <summary>
    /// A causal-loop diagram: members around a circle, clockwise from the top, in loop order, so each feedback loop reads
    /// as a ring and its links bow outward. Each member takes an arc as long as its larger side plus a gap.
    /// </summary>
    private static void Circle(CanvasState state, LayoutEngine layout, Region region, List<(string Id, int W, int H, DateTimeOffset At)> members)
    {
        var sizes = members.ToDictionary(m => m.Id, m => (m.W, m.H), StringComparer.Ordinal);
        var order = CausalLoops.CircleOrder(state, region.Id, [.. members.Select(m => m.Id)]);
        var arcs = order.Select(id => (double)Math.Max(sizes[id].W, sizes[id].H) + CircleGap).ToList();
        var radius = order.Count == 1 ? 0 : Math.Max(120, arcs.Sum() / (2 * Math.PI));
        var centres = new List<(string Id, double X, double Y)>();
        double walked = 0;
        for (var i = 0; i < order.Count; i++)
        {
            var angle = -Math.PI / 2 + (walked + arcs[i] / 2) / Math.Max(1, arcs.Sum()) * 2 * Math.PI;
            walked += arcs[i];
            centres.Add((order[i], radius * Math.Cos(angle), radius * Math.Sin(angle)));
        }
        var minX = centres.Min(c => c.X - sizes[c.Id].W / 2.0);
        var minY = centres.Min(c => c.Y - sizes[c.Id].H / 2.0);
        foreach (var (id, x, y) in centres)
            Move(state, layout, id, (int)Math.Round(Geometry.Padding + x - sizes[id].W / 2.0 - minX), (int)Math.Round(Geometry.Header + y - sizes[id].H / 2.0 - minY));
        if (region.Cols != 1) state.Regions[region.Id] = state.Regions[region.Id] with { Cols = 1 };
    }

    private static void Move(CanvasState state, LayoutEngine layout, string id, int x, int y)
    {
        if (EntityIds.KindOf(id) == EntityKind.Card)
        {
            var card = state.Cards[id];
            if (card.X == x && card.Y == y) return;
            state.Cards[id] = card with { X = x, Y = y };
        }
        else
        {
            var region = state.Regions[id];
            if (region.X == x && region.Y == y) return;
            state.Regions[id] = region with { X = x, Y = y };
        }
        layout.Touch(id);
    }

    /// <summary>Reverse the edges that close a cycle (found by depth-first search in creation order).</summary>
    private static void BreakCycles(List<Item> items, List<(Item From, Item To)> edges)
    {
        var outgoing = items.ToDictionary(i => i, _ => new List<int>());
        for (var e = 0; e < edges.Count; e++) outgoing[edges[e].From].Add(e);
        var state = new Dictionary<Item, int>(); // 1 on the stack, 2 done
        void Visit(Item item)
        {
            state[item] = 1;
            foreach (var e in outgoing[item])
            {
                var to = edges[e].To;
                if (state.GetValueOrDefault(to) == 1) edges[e] = (to, item);
                else if (state.GetValueOrDefault(to) == 0) Visit(to);
            }
            state[item] = 2;
        }
        // Start from the sources (a flowchart's start), so a loop back to an earlier step is the edge that gets reversed.
        var targets = edges.Select(e => e.To).ToHashSet();
        foreach (var item in items.Where(i => !targets.Contains(i)).Concat(items)) if (state.GetValueOrDefault(item) == 0) Visit(item);
    }

    /// <summary>Longest-path ranks, then sources pulled down next to their first successor so they do not all pile on rank 0.</summary>
    private static void Rank(List<Item> items)
    {
        var remaining = items.ToDictionary(i => i, i => i.Up.Count);
        var ready = new Queue<Item>(items.Where(i => i.Up.Count == 0));
        var topo = new List<Item>();
        while (ready.TryDequeue(out var item))
        {
            topo.Add(item);
            foreach (var next in item.Down)
            {
                next.Rank = Math.Max(next.Rank, item.Rank + 1);
                if (--remaining[next] == 0) ready.Enqueue(next);
            }
        }
        for (var i = topo.Count - 1; i >= 0; i--)
        {
            var item = topo[i];
            if (item.Up.Count == 0 && item.Down.Count > 0) item.Rank = item.Down.Min(n => n.Rank) - 1;
        }
        var lowest = items.Min(i => i.Rank);
        foreach (var item in items) item.Rank -= lowest;
    }

    /// <summary>Layers in initial order (creation order, then dummies after their source), with dummy nodes on long edges.</summary>
    private static List<List<Item>> AddDummies(List<Item> items)
    {
        var count = items.Max(i => i.Rank) + 1;
        var layers = Enumerable.Range(0, count).Select(_ => new List<Item>()).ToList();
        foreach (var item in items) layers[item.Rank].Add(item);
        var serial = 0;
        foreach (var from in items.ToList())
            foreach (var to in from.Down.ToList())
            {
                if (to.Rank - from.Rank <= 1) continue;
                from.Down.Remove(to);
                to.Up.Remove(from);
                var previous = from;
                for (var rank = from.Rank + 1; rank < to.Rank; rank++)
                {
                    var dummy = new Item($"~{serial++}", DummySize, DummySize, true) { Rank = rank };
                    layers[rank].Add(dummy);
                    previous.Down.Add(dummy);
                    dummy.Up.Add(previous);
                    previous = dummy;
                }
                previous.Down.Add(to);
                to.Up.Add(previous);
            }
        return [.. layers.Where(l => l.Count > 0)];
    }

    /// <summary>Barycenter sweeps, down then up, keeping the order with the fewest crossings.</summary>
    private static void Order(List<List<Item>> layers)
    {
        var best = layers.Select(l => l.ToList()).ToList();
        var bestCrossings = Crossings(layers);
        for (var sweep = 0; sweep < 8 && bestCrossings > 0; sweep++)
        {
            var downward = sweep % 2 == 0;
            for (var k = 0; k < layers.Count; k++)
            {
                var index = downward ? k : layers.Count - 1 - k;
                var neighbourLayer = downward ? index - 1 : index + 1;
                if (neighbourLayer < 0 || neighbourLayer >= layers.Count) continue;
                var position = layers[neighbourLayer].Select((item, i) => (item, i)).ToDictionary(p => p.item, p => (double)p.i);
                var current = layers[index];
                var keys = current.Select((item, i) =>
                {
                    var neighbours = (downward ? item.Up : item.Down).Where(position.ContainsKey).ToList();
                    return (item, key: neighbours.Count == 0 ? i : neighbours.Average(n => position[n]), i);
                }).OrderBy(x => x.key).ThenBy(x => x.i).Select(x => x.item).ToList();
                layers[index] = keys;
            }
            var crossings = Crossings(layers);
            if (crossings < bestCrossings) { bestCrossings = crossings; best = layers.Select(l => l.ToList()).ToList(); }
        }
        for (var i = 0; i < layers.Count; i++) layers[i] = best[i];
    }

    private static int Crossings(List<List<Item>> layers)
    {
        var total = 0;
        for (var r = 0; r + 1 < layers.Count; r++)
        {
            var lower = layers[r + 1].Select((item, i) => (item, i)).ToDictionary(p => p.item, p => p.i);
            var pairs = layers[r].SelectMany((item, i) => item.Down.Where(lower.ContainsKey).Select(d => (Upper: i, Lower: lower[d]))).ToList();
            for (var a = 0; a < pairs.Count; a++)
                for (var b = a + 1; b < pairs.Count; b++)
                    if ((pairs[a].Upper - pairs[b].Upper) * (pairs[a].Lower - pairs[b].Lower) < 0) total++;
        }
        return total;
    }

    /// <summary>Cross-axis positions: pack each layer, then repeatedly centre items on their neighbours without overlapping.</summary>
    private static void Place(List<List<Item>> layers)
    {
        var widest = layers.Max(Width);
        foreach (var layer in layers)
        {
            double x = (widest - Width(layer)) / 2.0;
            foreach (var item in layer) { item.Position = x; x += item.Cross + NodeGap; }
        }
        for (var pass = 0; pass < 6; pass++)
        {
            var downward = pass % 2 == 0;
            for (var k = 0; k < layers.Count; k++)
            {
                var layer = layers[downward ? k : layers.Count - 1 - k];
                var desired = layer.Select(item =>
                {
                    var neighbours = downward ? item.Up : item.Down;
                    if (neighbours.Count == 0) neighbours = downward ? item.Down : item.Up;
                    return neighbours.Count == 0 ? item.Position : neighbours.Average(n => n.Position + n.Cross / 2.0) - item.Cross / 2.0;
                }).ToArray();
                // Forward pass pushes right to remove overlaps; backward pass pulls left towards the desired spots.
                for (var i = 0; i < layer.Count; i++)
                    desired[i] = i == 0 ? desired[i] : Math.Max(desired[i], desired[i - 1] + layer[i - 1].Cross + NodeGap);
                for (var i = layer.Count - 2; i >= 0; i--)
                    desired[i] = Math.Min(desired[i], desired[i + 1] - layer[i].Cross - NodeGap);
                for (var i = 0; i < layer.Count; i++) layer[i].Position = desired[i];
            }
        }
    }

    private static double Width(List<Item> layer) => layer.Sum(i => i.Cross) + NodeGap * Math.Max(0, layer.Count - 1);
}
