using System.Globalization;
using System.Security;
using System.Text;
using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// Draws a region as a standalone SVG on the server, from the positions the server already computed: shapes, cards,
/// groups, orthogonal connections with their styles and labels. Every item is a <c>&lt;g data-id&gt;</c>, so a viewer can
/// light up a walkthrough step by toggling classes. Colours are CSS variables with a light default and a dark scheme,
/// so the same drawing works in the app, in an export and inside a chat. All text is XML-escaped (card text is untrusted).
/// Rendered (Mermaid) cards draw as a titled frame: their drawing needs the browser.
/// </summary>
public static class SvgRenderer
{
    private const int Margin = 16;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record Box(string Id, double X, double Y, double W, double H);

    public static string Render(CanvasState state, Region region)
    {
        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        var groups = state.RegionsIn(region.Id).OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
        foreach (var card in state.CardsIn(region.Id)) boxes[card.Id] = new(card.Id, card.X, card.Y, card.W, card.H);
        foreach (var group in groups)
        {
            boxes[group.Id] = new(group.Id, group.X, group.Y, group.W, group.H);
            foreach (var card in state.CardsIn(group.Id)) boxes[card.Id] = new(card.Id, group.X + card.X, group.Y + card.Y, card.W, card.H);
        }
        var (w, h) = (region.W + 2 * Margin, region.H + 2 * Margin);
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" width=\"{w}\" height=\"{h}\" font-family=\"Inter, system-ui, -apple-system, Segoe UI, sans-serif\" role=\"img\" aria-label=\"{Esc(region.Title)}\">");
        svg.Append("<title>").Append(Esc(region.Title)).Append("</title>");
        svg.Append(Style);
        svg.Append("<defs><marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerUnits=\"userSpaceOnUse\" markerWidth=\"11\" markerHeight=\"11\" orient=\"auto-start-reverse\"><path d=\"M0 0 L10 5 L0 10 z\" class=\"arrowhead\"/></marker>" +
            "<marker id=\"loop-arrow\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerUnits=\"userSpaceOnUse\" markerWidth=\"8\" markerHeight=\"8\" orient=\"auto\"><path d=\"M0 0 L10 5 L0 10 z\" class=\"loop-arrowhead\"/></marker></defs>");
        svg.Append($"<g transform=\"translate({Margin} {Margin})\">");
        svg.Append($"<rect class=\"region\" x=\"0\" y=\"0\" width=\"{region.W}\" height=\"{region.H}\" rx=\"14\"/>");
        svg.Append($"<text class=\"region-title\" x=\"24\" y=\"30\">{Esc(region.Title)}</text>");

        foreach (var group in groups)
        {
            var b = boxes[group.Id];
            svg.Append($"<g class=\"item group\" data-id=\"{group.Id}\"><rect x=\"{N(b.X)}\" y=\"{N(b.Y)}\" width=\"{N(b.W)}\" height=\"{N(b.H)}\" rx=\"12\"/>");
            svg.Append($"<text class=\"group-title\" x=\"{N(b.X + 20)}\" y=\"{N(b.Y + 28)}\">{Esc(group.Title)}</text></g>");
        }

        // Connections under the shapes, as in the app. A causal-loop diagram curves them, bowing out of the circle.
        var loops = region.Layout == RegionLayouts.Loops;
        foreach (var c in state.Connections.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            if (!boxes.TryGetValue(c.SourceId, out var from) || !boxes.TryGetValue(c.TargetId, out var to)) continue;
            var ends = c.Ends ?? (ConnectionStyle.Directional(c.Kind) ? EndStyles.End : EndStyles.None);
            var (path, labelX, labelY) = loops ? Curve(from, to) : Route(from, to);
            var markers = (ends is EndStyles.End or EndStyles.Both ? " marker-end=\"url(#arrow)\"" : "") + (ends is EndStyles.Start or EndStyles.Both ? " marker-start=\"url(#arrow)\"" : "");
            svg.Append($"<g class=\"item edge line-{c.Line ?? LineStyles.Solid} kind-{c.Kind}\" data-id=\"{c.Id}\" data-from=\"{c.SourceId}\" data-to=\"{c.TargetId}\"><path d=\"{path}\"{markers}/>");
            if (c.Label is { } label)
            {
                var width = label.Length * 6.6 + 10;
                svg.Append($"<rect class=\"edge-label-bg\" x=\"{N(labelX - width / 2)}\" y=\"{N(labelY - 9)}\" width=\"{N(width)}\" height=\"18\" rx=\"4\"/>");
                svg.Append($"<text class=\"edge-label\" x=\"{N(labelX)}\" y=\"{N(labelY + 4)}\" text-anchor=\"middle\">{Esc(label)}</text>");
            }
            svg.Append("</g>");
        }

        foreach (var card in state.Cards.Values.Where(c => boxes.ContainsKey(c.Id)).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
            svg.Append(CardSvg(card, boxes[card.Id], card.RegionId == region.Id ? null : card.RegionId));
        if (loops)
        {
            var direct = boxes.Values.Where(b => state.Cards.GetValueOrDefault(b.Id)?.RegionId == region.Id || state.Regions.GetValueOrDefault(b.Id)?.ParentId == region.Id).ToList();
            var middle = (X: direct.Average(b => b.X + b.W / 2), Y: direct.Average(b => b.Y + b.H / 2));
            foreach (var loop in CausalLoops.Find(state, region.Id)) svg.Append(LoopBadge(loop, boxes, middle));
        }

        svg.Append("</g></svg>");
        return svg.ToString();
    }

    private static string CardSvg(Card card, Box b, string? group)
    {
        var tint = card.Tint is null ? "" : $" tint-{card.Tint}";
        var key = card.Emphasis == Emphases.Key ? " key" : "";
        var href = card.Url is { } url && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ? $" data-href=\"{Esc(url)}\"" : "";
        var sb = new StringBuilder($"<g class=\"item node{tint}{key}{(href.Length > 0 ? " linked" : "")}\" data-id=\"{card.Id}\"{(group is null ? "" : $" data-group=\"{group}\"")}{href}>");
        if (card.Shape is { } shape)
        {
            sb.Append(Outline(shape, b));
            var (textY, maxWidth) = shape switch
            {
                Shapes.Person => (b.Y + b.H - 12, b.W),
                Shapes.Diamond => (b.Y + b.H / 2, b.W * 0.6),
                Shapes.Cylinder => (b.Y + b.H / 2 + 8, b.W - 24),
                _ => (b.Y + b.H / 2, b.W - 28)
            };
            if (ShapeGeometry.ShowsIcon(shape, card.Icon) && IconPaths.Markup.TryGetValue(card.Icon!, out var icon))
            {
                // The icon sits above the label; together they stay centred where the label alone would be.
                textY += ShapeGeometry.IconRow / 2.0;
                var iconY = textY - Lines(card.Title, maxWidth) * 9 - 26;
                sb.Append($"<g class=\"icon\" transform=\"translate({N(b.X + b.W / 2 - 10)} {N(iconY)}) scale(0.8333)\">{icon}</g>");
            }
            sb.Append(Lines(card.Title, b.X + b.W / 2, textY, maxWidth, "label", center: true, maxLines: 3));
        }
        else
        {
            sb.Append($"<rect class=\"card\" x=\"{N(b.X)}\" y=\"{N(b.Y)}\" width=\"{N(b.W)}\" height=\"{N(b.H)}\" rx=\"10\"/>");
            sb.Append(Lines(card.Title, b.X + 14, b.Y + 24, b.W - 28, "card-title", center: false, maxLines: 2));
            var body = card.Kind == CardKinds.Mermaid ? "Rendered diagram (open it in Sprawl)" : card.Kind == CardKinds.Code ? "" : FirstLine(card.Body);
            if (body.Length > 0 && b.H > 70)
                sb.Append(Lines(body, b.X + 14, b.Y + 46, b.W - 28, "card-body", center: false, maxLines: Math.Max(1, (int)((b.H - 56) / 17))));
        }
        return sb.Append("</g>").ToString();
    }

    /// <summary>The same outlines the app draws (canvas/shapes.tsx), inset by the stroke.</summary>
    private static string Outline(string shape, Box b)
    {
        double x0 = b.X + 1.5, y0 = b.Y + 1.5, x1 = b.X + b.W - 1.5, y1 = b.Y + b.H - 1.5, W = x1 - x0, H = y1 - y0, cx = b.X + b.W / 2, cy = b.Y + b.H / 2;
        string P(string d, string cls = "shape") => $"<path class=\"{cls}\" d=\"{d}\"/>";
        return shape switch
        {
            Shapes.Rectangle => $"<rect class=\"shape\" x=\"{N(x0)}\" y=\"{N(y0)}\" width=\"{N(W)}\" height=\"{N(H)}\" rx=\"3\"/>",
            Shapes.Rounded => $"<rect class=\"shape\" x=\"{N(x0)}\" y=\"{N(y0)}\" width=\"{N(W)}\" height=\"{N(H)}\" rx=\"{N(Math.Min(14, H / 3))}\"/>",
            Shapes.Terminal => $"<rect class=\"shape\" x=\"{N(x0)}\" y=\"{N(y0)}\" width=\"{N(W)}\" height=\"{N(H)}\" rx=\"{N(H / 2)}\"/>",
            Shapes.Diamond => P($"M{N(cx)} {N(y0)} L{N(x1)} {N(cy)} L{N(cx)} {N(y1)} L{N(x0)} {N(cy)} Z"),
            Shapes.Circle or Shapes.Ellipse => $"<ellipse class=\"shape\" cx=\"{N(cx)}\" cy=\"{N(cy)}\" rx=\"{N(W / 2)}\" ry=\"{N(H / 2)}\"/>",
            Shapes.Cylinder => Cylinder(x0, y0, x1, y1, W, H),
            Shapes.Queue => Queue(x0, y0, x1, y1, W, H),
            Shapes.Parallelogram => P($"M{N(x0 + Math.Min(18, W / 6))} {N(y0)} L{N(x1)} {N(y0)} L{N(x1 - Math.Min(18, W / 6))} {N(y1)} L{N(x0)} {N(y1)} Z"),
            Shapes.Hexagon => Hexagon(x0, y0, x1, y1, W, cy),
            Shapes.Document => P($"M{N(x0)} {N(y0)} L{N(x1)} {N(y0)} L{N(x1)} {N(y1 - Math.Min(10, H / 6))} C{N(x1 - W / 4)} {N(y1 - 2.4 * Math.Min(10, H / 6))} {N(x0 + W / 2)} {N(y1 + Math.Min(10, H / 6))} {N(x0)} {N(y1 - Math.Min(10, H / 6))} Z"),
            Shapes.Subroutine => $"<rect class=\"shape\" x=\"{N(x0)}\" y=\"{N(y0)}\" width=\"{N(W)}\" height=\"{N(H)}\" rx=\"3\"/>" + P($"M{N(x0 + 10)} {N(y0)} L{N(x0 + 10)} {N(y1)} M{N(x1 - 10)} {N(y0)} L{N(x1 - 10)} {N(y1)}", "detail"),
            Shapes.Cloud => Cloud(x0, y0, W, H),
            Shapes.Person => Person(cx, y0, W),
            _ => ""
        };
    }

    private static string Cylinder(double x0, double y0, double x1, double y1, double W, double H)
    {
        var ry = Math.Min(12, H / 6);
        return $"<path class=\"shape\" d=\"M{N(x0)} {N(y0 + ry)} L{N(x0)} {N(y1 - ry)} A{N(W / 2)} {N(ry)} 0 0 0 {N(x1)} {N(y1 - ry)} L{N(x1)} {N(y0 + ry)} A{N(W / 2)} {N(ry)} 0 0 0 {N(x0)} {N(y0 + ry)} Z\"/>"
            + $"<path class=\"detail\" d=\"M{N(x0)} {N(y0 + ry)} A{N(W / 2)} {N(ry)} 0 0 0 {N(x1)} {N(y0 + ry)}\"/>";
    }

    private static string Queue(double x0, double y0, double x1, double y1, double W, double H)
    {
        var rx = Math.Min(14, W / 8);
        return $"<path class=\"shape\" d=\"M{N(x0 + rx)} {N(y0)} L{N(x1 - rx)} {N(y0)} A{N(rx)} {N(H / 2)} 0 0 1 {N(x1 - rx)} {N(y1)} L{N(x0 + rx)} {N(y1)} A{N(rx)} {N(H / 2)} 0 0 1 {N(x0 + rx)} {N(y0)} Z\"/>"
            + $"<path class=\"detail\" d=\"M{N(x1 - rx)} {N(y0)} A{N(rx)} {N(H / 2)} 0 0 0 {N(x1 - rx)} {N(y1)}\"/>";
    }

    private static string Hexagon(double x0, double y0, double x1, double y1, double W, double cy)
    {
        var s = Math.Min(18, W / 6);
        return $"<path class=\"shape\" d=\"M{N(x0 + s)} {N(y0)} L{N(x1 - s)} {N(y0)} L{N(x1)} {N(cy)} L{N(x1 - s)} {N(y1)} L{N(x0 + s)} {N(y1)} L{N(x0)} {N(cy)} Z\"/>";
    }

    private static string Cloud(double x0, double y0, double W, double H)
    {
        string P(double px, double py) => $"{N(x0 + px * W)} {N(y0 + py * H)}";
        return $"<path class=\"shape\" d=\"M{P(0.22, 0.86)} C{P(0.02, 0.86)} {P(0.0, 0.52)} {P(0.16, 0.46)} C{P(0.1, 0.18)} {P(0.38, 0.06)} {P(0.5, 0.22)} " +
               $"C{P(0.58, 0.0)} {P(0.88, 0.06)} {P(0.86, 0.34)} C{P(1.02, 0.38)} {P(1.04, 0.84)} {P(0.8, 0.86)} Z\"/>";
    }

    private static string Person(double cx, double y0, double W)
    {
        var r = Math.Min(14, W / 5);
        return $"<circle class=\"shape\" cx=\"{N(cx)}\" cy=\"{N(y0 + r)}\" r=\"{N(r)}\"/>" +
               $"<path class=\"shape\" d=\"M{N(cx - r * 1.9)} {N(y0 + r * 4.4)} C{N(cx - r * 1.9)} {N(y0 + r * 2.4)} {N(cx + r * 1.9)} {N(y0 + r * 2.4)} {N(cx + r * 1.9)} {N(y0 + r * 4.4)} Z\"/>";
    }

    /// <summary>
    /// A curve for a causal link: it bows to the left of its direction (outward, for a loop drawn clockwise), so the two
    /// links of a two-node loop never overlap. It leaves each box where the line toward the curve's control point does.
    /// </summary>
    public static (string Path, double LabelX, double LabelY) Curve(Box from, Box to)
    {
        double ax = from.X + from.W / 2, ay = from.Y + from.H / 2, bx = to.X + to.W / 2, by = to.Y + to.H / 2;
        double dx = bx - ax, dy = by - ay, length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double cx = (ax + bx) / 2 + dy / length * length * 0.22, cy = (ay + by) / 2 - dx / length * length * 0.22;
        var (sx, sy) = Clip(from, cx, cy);
        var (tx, ty) = Clip(to, cx, cy);
        return ($"M{N(sx)} {N(sy)} Q{N(cx)} {N(cy)} {N(tx)} {N(ty)}", 0.25 * sx + 0.5 * cx + 0.25 * tx, 0.25 * sy + 0.5 * cy + 0.25 * ty);
    }

    /// <summary>Where the ray from a box's centre toward a point leaves the box.</summary>
    private static (double X, double Y) Clip(Box box, double px, double py)
    {
        double cx = box.X + box.W / 2, cy = box.Y + box.H / 2, dx = px - cx, dy = py - cy;
        if (dx == 0 && dy == 0) return (cx, cy);
        var t = Math.Min(dx == 0 ? double.MaxValue : box.W / 2 / Math.Abs(dx), dy == 0 ? double.MaxValue : box.H / 2 / Math.Abs(dy));
        return (cx + dx * t, cy + dy * t);
    }

    /// <summary>A loop's name (R1, B2) in a circle at the middle of its members, with an arrow turning the way the loop runs.</summary>
    private static string LoopBadge(CausalLoops.Loop loop, Dictionary<string, Box> boxes, (double X, double Y) middle)
    {
        var centres = loop.Members.Where(boxes.ContainsKey).Select(id => (X: boxes[id].X + boxes[id].W / 2, Y: boxes[id].Y + boxes[id].H / 2)).ToList();
        if (centres.Count < 2) return "";
        double mx = centres.Average(c => c.X), my = centres.Average(c => c.Y), area = 0;
        // A two-variable loop's middle lies between its two links: its badge moves over halfway toward the circle's centre.
        if (centres.Count == 2) { mx += (middle.X - mx) * 0.55; my += (middle.Y - my) * 0.55; }
        for (var i = 0; i < centres.Count; i++) { var (a, b) = (centres[i], centres[(i + 1) % centres.Count]); area += a.X * b.Y - b.X * a.Y; }
        var sweep = area >= 0 ? 1 : 0; // screen coordinates: a positive area runs clockwise
        var cls = loop.Polarity switch { 'R' => "loop-r", 'B' => "loop-b", _ => "loop-u" };
        const double r = 19;
        var startX = sweep == 1 ? mx + r : mx - r; // three quarters of a turn, ending at the top with the arrow
        return $"<g class=\"item loop {cls}\" data-loop=\"{loop.Name}\"><circle cx=\"{N(mx)}\" cy=\"{N(my)}\" r=\"15\"/>" +
               $"<path d=\"M{N(startX)} {N(my)} A{N(r)} {N(r)} 0 1 {sweep} {N(mx)} {N(my - r)}\" marker-end=\"url(#loop-arrow)\"/>" +
               $"<text x=\"{N(mx)}\" y=\"{N(my + 4.5)}\" text-anchor=\"middle\">{loop.Name}</text></g>";
    }

    /// <summary>An orthogonal route between the facing sides of two boxes, and a point for its label.</summary>
    public static (string Path, double LabelX, double LabelY) Route(Box from, Box to)
    {
        var (sx, sy, sh) = Border(from, to);
        var (tx, ty, th) = Border(to, from);
        string d;
        double lx, ly;
        if (sh && th) { var mx = (sx + tx) / 2; d = $"M{N(sx)} {N(sy)} H{N(mx)} V{N(ty)} H{N(tx)}"; (lx, ly) = (mx, (sy + ty) / 2); }
        else if (!sh && !th) { var my = (sy + ty) / 2; d = $"M{N(sx)} {N(sy)} V{N(my)} H{N(tx)} V{N(ty)}"; (lx, ly) = ((sx + tx) / 2, my); }
        else if (sh) { d = $"M{N(sx)} {N(sy)} H{N(tx)} V{N(ty)}"; (lx, ly) = ((sx + tx) / 2, sy); }
        else { d = $"M{N(sx)} {N(sy)} V{N(ty)} H{N(tx)}"; (lx, ly) = (sx, (sy + ty) / 2); }
        return (d, lx, ly);
    }

    /// <summary>Where the line between centres leaves the box (as the app's floating edges do), and whether that side is left/right.</summary>
    private static (double X, double Y, bool Horizontal) Border(Box box, Box toward)
    {
        double cx = box.X + box.W / 2, cy = box.Y + box.H / 2, dx = toward.X + toward.W / 2 - cx, dy = toward.Y + toward.H / 2 - cy;
        if (dx == 0 && dy == 0) return (cx, box.Y, false);
        var (hw, hh) = (box.W / 2, box.H / 2);
        var horizontal = Math.Abs(dx) * hh > Math.Abs(dy) * hw;
        return horizontal ? (dx > 0 ? box.X + box.W : box.X, cy, true) : (cx, dy > 0 ? box.Y + box.H : box.Y, false);
    }

    private static int Lines(string text, double maxWidth) => Math.Min(3, Wrap(text, Math.Max(4, (int)(maxWidth / 7.0)), 3).Count);

    private static string Lines(string text, double x, double y, double maxWidth, string cls, bool center, int maxLines)
    {
        var perLine = Math.Max(4, (int)(maxWidth / (cls == "card-body" ? 6.3 : 7.0)));
        var lines = Wrap(text, perLine, maxLines);
        var lineHeight = cls == "card-body" ? 17 : 18;
        var first = center ? y - (lines.Count - 1) * lineHeight / 2.0 + 4.5 : y;
        var anchor = center ? " text-anchor=\"middle\"" : "";
        var sb = new StringBuilder($"<text class=\"{cls}\"{anchor}>");
        for (var i = 0; i < lines.Count; i++) sb.Append($"<tspan x=\"{N(x)}\" y=\"{N(first + i * lineHeight)}\">{Esc(lines[i])}</tspan>");
        return sb.Append("</text>").ToString();
    }

    private static List<string> Wrap(string text, int perLine, int maxLines)
    {
        var words = text.Split((char[])[' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > perLine) { lines.Add(line.ToString()); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(word.Length > perLine ? word[..(perLine - 1)] + "…" : word);
        }
        if (line.Length > 0) lines.Add(line.ToString());
        if (lines.Count > maxLines) { lines = lines[..maxLines]; lines[^1] = lines[^1].TrimEnd('.', ',') + "…"; }
        return lines;
    }

    private static string FirstLine(string body)
    {
        var text = string.Join(' ', body.Split('\n').Select(l => l.Trim().TrimStart('#', '-', '*', '>', ' ')).Where(l => l.Length > 0));
        return text.Replace("[[", "", StringComparison.Ordinal).Replace("]]", "", StringComparison.Ordinal).Replace("**", "", StringComparison.Ordinal);
    }

    private static string Esc(string text) => SecurityElement.Escape(text) ?? "";
    private static string N(double value) => Math.Round(value, 1).ToString(Inv);

    private const string Style = """
        <style>
        svg { --bg: #f7f7f8; --region: #eef0f3; --surface: #ffffff; --stroke: #8a8f98; --text: #1f2328; --muted: #5d636b; --accent: #2f6fde; --group: #b9bec6; --loop-r: #b5651d; --loop-b: #23727d; }
        @media (prefers-color-scheme: dark) { svg { --bg: #1b1c1e; --region: #232529; --surface: #2a2c30; --stroke: #8e939b; --text: #ececee; --muted: #a9adb4; --accent: #6b9cf0; --group: #4a4e55; --loop-r: #f0a35e; --loop-b: #5cc4cf; } }
        svg.theme-light { --bg: #f7f7f8; --region: #eef0f3; --surface: #ffffff; --stroke: #8a8f98; --text: #1f2328; --muted: #5d636b; --accent: #2f6fde; --group: #b9bec6; --loop-r: #b5651d; --loop-b: #23727d; }
        svg.theme-dark { --bg: #1b1c1e; --region: #232529; --surface: #2a2c30; --stroke: #8e939b; --text: #ececee; --muted: #a9adb4; --accent: #6b9cf0; --group: #4a4e55; --loop-r: #f0a35e; --loop-b: #5cc4cf; }
        .region { fill: var(--region); }
        .region-title { fill: var(--text); font-size: 14px; font-weight: 600; }
        .group rect { fill: none; stroke: var(--group); stroke-width: 1.5; stroke-dasharray: 5 4; }
        .group-title { fill: var(--muted); font-size: 13px; font-weight: 600; }
        .shape { fill: var(--surface); stroke: var(--stroke); stroke-width: 1.5; stroke-linejoin: round; }
        .detail { fill: none; stroke: var(--stroke); stroke-width: 1.5; }
        .icon { fill: none; stroke: var(--muted); stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
        .key .shape, .key .card { stroke-width: 2.5; }
        .card { fill: var(--surface); stroke: var(--group); stroke-width: 1; }
        .label { fill: var(--text); font-size: 13px; font-weight: 550; }
        .card-title { fill: var(--text); font-size: 13px; font-weight: 600; }
        .card-body { fill: var(--muted); font-size: 12px; }
        .edge path { fill: none; stroke: var(--stroke); stroke-width: 1.5; }
        .arrowhead { fill: var(--stroke); }
        .line-dashed path { stroke-dasharray: 6 4; }
        .line-dotted path { stroke-dasharray: 1 4; stroke-linecap: round; }
        .line-thick path { stroke-width: 2.75; }
        .edge-label-bg { fill: var(--region); }
        .edge-label { fill: var(--text); font-size: 11.5px; }
        .tint-blue .shape { fill: #e3ecfb; stroke: #4a7fd6; } .tint-green .shape { fill: #e1f3e8; stroke: #3f9a63; }
        .tint-amber .shape { fill: #fbf0d9; stroke: #c98d14; } .tint-red .shape { fill: #fbe4e2; stroke: #d0564b; }
        .tint-violet .shape { fill: #efe7fa; stroke: #8a62c9; } .tint-grey .shape { fill: #eeeff1; stroke: #8a8f98; }
        .loop circle { fill: var(--surface); stroke-width: 1.5; } .loop path { fill: none; stroke-width: 1.5; } .loop text { font-size: 12px; font-weight: 700; }
        .loop-r circle, .loop-r path { stroke: var(--loop-r); } .loop-r text { fill: var(--loop-r); }
        .loop-b circle, .loop-b path { stroke: var(--loop-b); } .loop-b text { fill: var(--loop-b); }
        .loop-u circle, .loop-u path { stroke: var(--stroke); } .loop-u text { fill: var(--muted); }
        .loop-arrowhead { fill: var(--stroke); }
        .linked { cursor: pointer; } .linked .card-title { text-decoration: underline; text-decoration-color: var(--muted); }
        .item { transition: opacity 250ms ease; }
        .walking .item.dim { opacity: 0.15; }
        .walking .node.lit .shape, .walking .node.lit .card { stroke: var(--accent); stroke-width: 2.5; }
        .walking .edge.lit path { stroke: var(--accent); stroke-width: 2.5; stroke-dasharray: 7 5; animation: flow 0.9s linear infinite; }
        .walking .edge.lit .arrowhead { fill: var(--accent); }
        .walking .item.mark-down .shape, .walking .item.mark-down .card { stroke: #d0564b; stroke-width: 2.5; stroke-dasharray: 6 4; } .walking .edge.mark-down path { stroke: #d0564b; stroke-dasharray: 2 5; }
        .walking .item.mark-slow .shape, .walking .item.mark-slow .card { stroke: #c98d14; stroke-width: 2.5; } .walking .edge.mark-slow path { stroke: #c98d14; stroke-width: 2.5; }
        .walking .item.mark-hot .shape, .walking .item.mark-hot .card { stroke: #e0632b; stroke-width: 3; fill: #fde9dc; } .walking .edge.mark-hot path { stroke: #e0632b; stroke-width: 3; }
        .walking .item.mark-added .shape, .walking .item.mark-added .card { stroke: #3f9a63; stroke-width: 2.5; fill: #e1f3e8; } .walking .edge.mark-added path { stroke: #3f9a63; stroke-width: 2.5; }
        .walking .item.mark-removed { opacity: 0.35; } .walking .item.mark-removed .shape, .walking .item.mark-removed .card { stroke-dasharray: 3 4; } .walking .edge.mark-removed path { stroke-dasharray: 3 4; }
        .walking .item.mark-changed .shape, .walking .item.mark-changed .card { stroke: #8a62c9; stroke-width: 2.5; } .walking .edge.mark-changed path { stroke: #8a62c9; stroke-width: 2.5; }
        @keyframes flow { to { stroke-dashoffset: -12; } }
        @media (prefers-reduced-motion: reduce) { .walking .edge.lit path { animation: none; } }
        </style>
        """;
}
