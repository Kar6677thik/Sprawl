using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>Geometry constants (8 px grid). The UI renders cards at exactly these sizes.</summary>
public static class Geometry
{
    public const int CardWidth = 280;
    public const int Gap = 16;
    public const int Pitch = CardWidth + Gap;
    public const int Padding = 24;
    public const int Header = 48;
    public const int RegionGap = 64;
    public const int MaxCols = 6;
    public const int CompactHeight = 88;
    public const int RegularHeight = 168;
    public const int TallHeight = 288;
    public const int MinRegionHeight = Header + CompactHeight + Padding;
    public const int FrontierWidth = 2400;
    public const double TallnessLimit = 1.5;

    public static int ContentWidth(int cols) => cols * Pitch - Gap;
    public static int MinRegionWidth(int cols) => 2 * Padding + ContentWidth(cols);
    public static int ColumnX(int col) => Padding + col * Pitch;

    public static string SizeFor(string kind, string body) =>
        kind == CardKinds.Code ? CardSizes.Tall : body.Length <= 140 ? CardSizes.Compact : body.Length <= 600 ? CardSizes.Regular : CardSizes.Tall;

    public static int HeightOf(string size) => size switch
    {
        CardSizes.Compact => CompactHeight,
        CardSizes.Regular => RegularHeight,
        _ => TallHeight
    };

    /// <summary>Columns for a new region, chosen from the number of cards it starts with.</summary>
    public static int ColsFor(int cardCount) => cardCount <= 4 ? 2 : cardCount <= 12 ? 3 : 4;

    /// <summary>The size a new region will grow to once the given cards are placed in it (masonry simulation).</summary>
    public static (int W, int H) EstimateRegion(int cols, IEnumerable<int> cardHeights)
    {
        var bottoms = Enumerable.Repeat(Header, cols).ToArray();
        foreach (var height in cardHeights)
        {
            var col = Array.IndexOf(bottoms, bottoms.Min());
            bottoms[col] += height + Gap;
        }
        var content = bottoms.Max() == Header ? Header : bottoms.Max() - Gap;
        return (MinRegionWidth(cols), Math.Max(MinRegionHeight, content + Padding));
    }
}

/// <summary>
/// Deterministic server-side placement. Rules: manual items never move; auto items may translate (never reshape) to make
/// room; additions append and never reflow. All coordinates are integers; card positions are relative to their region,
/// sub-region positions to their parent, and top-level positions are canvas-absolute.
/// </summary>
public sealed class LayoutEngine(CanvasState state)
{
    private const string CanvasContainer = "";
    private readonly Dictionary<string, HashSet<string>> seeds = new(StringComparer.Ordinal);

    public List<string> Warnings { get; } = [];

    private readonly record struct Box(string Id, int X, int Y, int W, int H, bool Manual)
    {
        public int Right => X + W;
        public int Bottom => Y + H;
    }

    /// <summary>Record that an item's geometry or container changed. Call before removing and after adding or moving.</summary>
    public void Touch(string id)
    {
        var container = ContainerOf(id);
        if (container is null) return;
        if (!seeds.TryGetValue(container, out var set)) seeds[container] = set = new(StringComparer.Ordinal);
        set.Add(id);
    }

    /// <summary>Resolve overlaps and refit region bounds for everything touched since the last call, innermost first.</summary>
    public void Settle()
    {
        for (var level = 2; level >= 0; level--)
        {
            foreach (var container in seeds.Keys.Where(key => Level(key) == level).Order(StringComparer.Ordinal).ToList())
            {
                var touched = seeds[container];
                seeds.Remove(container);
                if (container != CanvasContainer && !state.Regions.ContainsKey(container)) continue;
                ResolveOverlaps(container, touched);
                if (container != CanvasContainer && FitRegion(container)) Touch(container);
            }
        }
        seeds.Clear();
    }

    /// <summary>Place an auto card at the bottom of the shortest column of its region (masonry).</summary>
    public void PlaceCard(string cardId)
    {
        var card = state.Cards[cardId];
        var regionId = card.RegionId ?? throw new InvalidOperationException("Only cards in a region are placed automatically.");
        var region = state.Regions[regionId];
        if (RegionLayouts.IsDiagram(region.Layout))
        {
            state.Cards[cardId] = card with { X = Geometry.Padding, Y = Geometry.Header, Placement = Placements.Auto };
            return;
        }
        var (col, y) = BestColumn(region, cardId, card.H);
        var contentWidth = Geometry.ContentWidth(region.Cols);
        if (region.Cols < Geometry.MaxCols && y + card.H - Geometry.Header > Geometry.TallnessLimit * contentWidth && CanWiden(region))
        {
            region = region with { Cols = region.Cols + 1 };
            state.Regions[regionId] = region;
            Touch(regionId);
            (col, y) = BestColumn(region, cardId, card.H);
        }
        state.Cards[cardId] = card with { X = Geometry.ColumnX(col), Y = y, W = Geometry.CardWidth, Placement = Placements.Auto };
    }

    /// <summary>
    /// Place a region (its W and H already set). Top-level regions go next to <paramref name="anchorId"/> when given,
    /// otherwise onto the frontier by bottom-left skyline packing. Sub-regions are packed inside their parent.
    /// </summary>
    public void PlaceRegion(string regionId, string? anchorId)
    {
        var region = state.Regions[regionId];
        var container = region.ParentId ?? CanvasContainer;
        var items = Children(container).Where(box => box.Id != regionId).ToList();
        int x, y;
        if (region.ParentId is { } diagramId && RegionLayouts.IsDiagram(state.Regions[diagramId].Layout))
            (x, y) = (Geometry.Padding, Geometry.Header);
        else if (region.ParentId is { } parentId)
        {
            var parent = state.Regions[parentId];
            (x, y) = Skyline(items, region.W, region.H, Geometry.Padding, Geometry.Header,
                Math.Max(Geometry.ContentWidth(parent.Cols), region.W), Geometry.Gap);
        }
        else if (anchorId is not null && state.Regions.TryGetValue(anchorId, out var anchor) && anchor.ParentId is null)
            (x, y) = NextTo(items, Geometry.RegionGap, BoxOf(anchor), region.W, region.H);
        else
        {
            var regions = items.Where(box => EntityIds.KindOf(box.Id) == EntityKind.Region).ToList();
            var mean = regions.Count == 0 ? region.W : (int)regions.Average(box => box.W);
            var originX = items.Count == 0 ? 0 : Math.Min(0, items.Min(box => box.X));
            var originY = items.Count == 0 ? 0 : Math.Min(0, items.Min(box => box.Y));
            (x, y) = Skyline(items, region.W, region.H, originX, originY,
                Math.Max(Geometry.FrontierWidth, 3 * mean + 2 * Geometry.RegionGap), Geometry.RegionGap);
        }
        state.Regions[regionId] = region with { X = x, Y = y };
    }

    /// <summary>Pairs of items that overlap within the same container (only possible between manual items).</summary>
    public static List<LayoutWarning> FindOverlaps(CanvasState state)
    {
        var engine = new LayoutEngine(state);
        var warnings = new List<LayoutWarning>();
        var containers = state.Regions.Keys.Append(CanvasContainer);
        foreach (var container in containers)
        {
            var boxes = engine.Children(container).ToList();
            for (var i = 0; i < boxes.Count; i++)
                for (var j = i + 1; j < boxes.Count; j++)
                    if (Overlaps(boxes[i], boxes[j], 0))
                        warnings.Add(new("overlap", boxes[i].Id, boxes[j].Id));
        }
        return warnings;
    }

    private (int Col, int Y) BestColumn(Region region, string cardId, int height)
    {
        var children = Children(region.Id).Where(box => box.Id != cardId).ToList();
        var best = (Col: 0, Y: int.MaxValue);
        for (var col = 0; col < region.Cols; col++)
        {
            var left = Geometry.ColumnX(col);
            var inColumn = children.Where(box => box.X < left + Geometry.CardWidth && box.Right > left).ToList();
            var y = Geometry.Header;
            foreach (var box in inColumn)
                if (!box.Manual && EntityIds.KindOf(box.Id) == EntityKind.Card) y = Math.Max(y, box.Bottom + Geometry.Gap);
            // Skip down past manual obstacles the card would intersect; auto sub-regions are pushed instead.
            for (var moved = true; moved;)
            {
                moved = false;
                foreach (var box in inColumn)
                {
                    if (!box.Manual || box.Y >= y + height + Geometry.Gap || box.Bottom + Geometry.Gap <= y) continue;
                    y = box.Bottom + Geometry.Gap;
                    moved = true;
                }
            }
            if (y < best.Y) best = (col, y);
        }
        return best;
    }

    private bool CanWiden(Region region)
    {
        var newWidth = Math.Max(region.W, Geometry.MinRegionWidth(region.Cols + 1));
        var delta = newWidth - region.W;
        if (delta <= 0) return true;
        var container = region.ParentId ?? CanvasContainer;
        if (region.ParentId is { } parentId && region.X + newWidth + Geometry.Padding > state.Regions[parentId].W) return false;
        var gap = container == CanvasContainer ? Geometry.RegionGap : Geometry.Gap;
        var strip = new Box("strip", region.X + region.W, region.Y, delta, region.H, false);
        return !Children(container).Any(box => box.Id != region.Id && Overlaps(strip, box, gap));
    }

    /// <summary>Refit a region to its contents now (the diagram pass needs a sub-region's size before placing its parent).</summary>
    public void Refit(string regionId)
    {
        if (FitRegion(regionId)) Touch(regionId);
    }

    private void ResolveOverlaps(string container, HashSet<string> touched)
    {
        // Inside a diagram the diagram layout owns positions; pushing items apart there would undo it.
        if (container != CanvasContainer && state.Regions.TryGetValue(container, out var owner) && RegionLayouts.IsDiagram(owner.Layout)) return;
        var gap = container == CanvasContainer ? Geometry.RegionGap : Geometry.Gap;
        var queue = new Queue<string>(touched.Order(StringComparer.Ordinal));
        var guard = 0;
        while (queue.TryDequeue(out var id))
        {
            if (++guard > 20_000) { Warnings.Add("Layout settling stopped early; run Tidy on this canvas."); return; }
            if (ContainerOf(id) != container) continue;
            var current = BoxOf(id);
            foreach (var other in Children(container).Where(box => box.Id != id).OrderBy(box => box.Y).ThenBy(box => box.X).ThenBy(box => box.Id, StringComparer.Ordinal).ToList())
            {
                var latest = BoxOf(other.Id);
                if (!Overlaps(current, latest, gap) || (current.Manual && latest.Manual)) continue;
                var (mover, anchor) = current.Manual ? (latest, current)
                    : latest.Manual ? (current, latest)
                    : latest.Y >= current.Y ? (latest, current) : (current, latest);
                MoveTo(mover.Id, mover.X, anchor.Bottom + gap);
                queue.Enqueue(mover.Id);
                if (mover.Id == id) break;
            }
        }
    }

    private bool FitRegion(string regionId)
    {
        var region = state.Regions[regionId];
        var children = Children(regionId).ToList();
        var width = Math.Max(Geometry.MinRegionWidth(region.Cols), children.Count == 0 ? 0 : children.Max(box => box.Right) + Geometry.Padding);
        var height = Math.Max(Geometry.MinRegionHeight, children.Count == 0 ? 0 : children.Max(box => box.Bottom) + Geometry.Padding);
        if (width == region.W && height == region.H) return false;
        state.Regions[regionId] = region with { W = width, H = height };
        return true;
    }

    private static (int X, int Y) Skyline(List<Box> items, int width, int height, int originX, int originY, int targetWidth, int gap)
    {
        if (items.Count == 0) return (originX, originY);
        var limit = originX + Math.Max(targetWidth, width);
        var candidates = items.Select(box => box.X).Concat(items.Select(box => box.Right + gap)).Append(originX)
            .Where(x => x >= originX && x + width <= limit).Distinct().Order();
        var best = (X: originX, Y: int.MaxValue);
        foreach (var x in candidates)
        {
            var y = originY;
            foreach (var box in items)
                if (box.X < x + width + gap && x < box.Right + gap) y = Math.Max(y, box.Bottom + gap);
            if (y < best.Y) best = (x, y);
        }
        if (best.Y == int.MaxValue) best = (originX, items.Max(box => box.Bottom) + gap);
        return best;
    }

    /// <summary>
    /// The free spot next to <paramref name="anchor"/> that needed the least sliding from directly adjacent. Right and below
    /// are preferred (in that order on ties); left and above carry a penalty so the canvas tends to grow right and down.
    /// </summary>
    private static (int X, int Y) NextTo(List<Box> items, int gap, Box anchor, int width, int height)
    {
        var candidates = new[]
        {
            (Spot: Slide(items, gap, anchor.Right + gap, anchor.Y, width, height, down: true), Penalty: 0),
            (Spot: Slide(items, gap, anchor.X, anchor.Bottom + gap, width, height, down: false), Penalty: 0),
            (Spot: Slide(items, gap, anchor.X - gap - width, anchor.Y, width, height, down: true), Penalty: SidePenalty),
            (Spot: Slide(items, gap, anchor.X, anchor.Y - gap - height, width, height, down: false), Penalty: SidePenalty)
        };
        var best = candidates.Select((candidate, order) => (candidate.Spot, Cost: candidate.Spot.Slid + candidate.Penalty, Order: order))
            .OrderBy(candidate => candidate.Cost).ThenBy(candidate => candidate.Order).First();
        return (best.Spot.X, best.Spot.Y);
    }

    private const int SidePenalty = 400;

    private static (int X, int Y, int Slid) Slide(List<Box> items, int gap, int x, int y, int width, int height, bool down)
    {
        var (startX, startY) = (x, y);
        for (var guard = 0; guard < 1000; guard++)
        {
            var probe = new Box("probe", x, y, width, height, false);
            var hits = items.Where(box => Overlaps(probe, box, gap)).ToList();
            if (hits.Count == 0) break;
            if (down) y = hits.Max(box => box.Bottom) + gap; else x = hits.Max(box => box.Right) + gap;
        }
        return (x, y, Math.Abs(x - startX) + Math.Abs(y - startY));
    }

    private static bool Overlaps(Box a, Box b, int gap) =>
        a.X < b.Right + gap && b.X < a.Right + gap && a.Y < b.Bottom + gap && b.Y < a.Bottom + gap;

    private IEnumerable<Box> Children(string container)
    {
        var regionId = container == CanvasContainer ? null : container;
        foreach (var card in state.Cards.Values)
            if (card.RegionId == regionId) yield return BoxOf(card);
        foreach (var region in state.Regions.Values)
            if (region.ParentId == regionId) yield return BoxOf(region);
    }

    private Box BoxOf(string id) => EntityIds.KindOf(id) == EntityKind.Card ? BoxOf(state.Cards[id]) : BoxOf(state.Regions[id]);
    private static Box BoxOf(Card card) => new(card.Id, card.X, card.Y, card.W, card.H, card.Placement == Placements.Manual || card.RegionId is null);
    private static Box BoxOf(Region region) => new(region.Id, region.X, region.Y, region.W, region.H, region.Placement == Placements.Manual);

    private void MoveTo(string id, int x, int y)
    {
        if (EntityIds.KindOf(id) == EntityKind.Card) state.Cards[id] = state.Cards[id] with { X = x, Y = y };
        else state.Regions[id] = state.Regions[id] with { X = x, Y = y };
    }

    private string? ContainerOf(string id) => EntityIds.KindOf(id) switch
    {
        EntityKind.Card => state.Cards.TryGetValue(id, out var card) ? card.RegionId ?? CanvasContainer : null,
        EntityKind.Region => state.Regions.TryGetValue(id, out var region) ? region.ParentId ?? CanvasContainer : null,
        _ => null
    };

    private int Level(string container) =>
        container == CanvasContainer ? 0 : state.Regions.TryGetValue(container, out var region) && region.ParentId is not null ? 2 : 1;
}
