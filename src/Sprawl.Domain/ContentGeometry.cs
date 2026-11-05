using System.Text.Json;
using System.Text.Json.Nodes;
using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// Sizes and checks for cards whose content has a shape of its own: tables (markdown) and charts (Vega-Lite).
/// Outside a diagram such a card spans as many masonry columns as it needs (at most <see cref="MaxSpan"/>).
/// </summary>
public static class ContentGeometry
{
    public const int MaxSpan = 3;

    /// <summary>The column-aligned width for content that wants <paramref name="width"/> pixels.</summary>
    public static int SpanWidth(int width)
    {
        var span = Math.Clamp((width + Geometry.Gap + Geometry.Pitch - 1) / Geometry.Pitch, 1, MaxSpan);
        return span * Geometry.Pitch - Geometry.Gap;
    }

    public static int SpanOf(int width) => Math.Max(1, (width + Geometry.Gap) / Geometry.Pitch);

    /// <summary>A markdown table's size: columns from its widest cells, rows at 30 px, plus the card header.</summary>
    public static (int W, int H) Table(string body)
    {
        var rows = body.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('|') || l.Contains(" | ", StringComparison.Ordinal)).ToList();
        if (rows.Count == 0) return (Geometry.CardWidth, Geometry.RegularHeight);
        var cells = rows.Select(r => r.Trim('|').Split('|').Select(c => c.Trim()).ToList()).ToList();
        var columns = cells.Max(c => c.Count);
        var width = 0;
        for (var col = 0; col < columns; col++)
            width += Math.Clamp(cells.Max(r => col < r.Count ? r[col].Length : 0) * 7 + 24, 56, 280);
        var dataRows = rows.Count(r => !r.Replace("|", "", StringComparison.Ordinal).Trim().All(c => c is '-' or ':' or ' '));
        return (Round(Math.Clamp(width + 28, Geometry.CardWidth, 1400)), Round(Math.Clamp(56 + dataRows * 30, 120, 1600)));
    }

    /// <summary>A chart's size from the spec's own width and height (defaults 480 × 300), plus the card header and axes.</summary>
    public static (int W, int H) Chart(string body)
    {
        int w = 480, h = 300;
        try
        {
            if (JsonNode.Parse(body) is JsonObject spec)
            {
                if (spec["width"] is JsonValue width && width.TryGetValue<double>(out var ww)) w = (int)ww;
                if (spec["height"] is JsonValue height && height.TryGetValue<double>(out var hh)) h = (int)hh;
            }
        }
        catch (JsonException) { }
        return (Round(Math.Clamp(w + 100, Geometry.CardWidth, 1400)), Round(Math.Clamp(h + 110, 200, 1400)));
    }

    /// <summary>A widget's size: from a leading <c>&lt;!-- size: 640x420 --&gt;</c> comment, else 576 × 400.</summary>
    public static (int W, int H) Widget(string body)
    {
        var match = System.Text.RegularExpressions.Regex.Match(body, @"^\s*<!--\s*size:\s*(\d{2,4})\s*x\s*(\d{2,4})\s*-->", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
        var (w, h) = match.Success ? (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)) : (576, 400);
        return (Round(Math.Clamp(w, Geometry.CardWidth, 1400)), Round(Math.Clamp(h + 48, 160, 1400)));
    }

    /// <summary>
    /// A chart body must be one Vega-Lite JSON object with its data inline: any <c>url</c> is refused, so a chart never
    /// fetches anything (the page's CSP would block it anyway) and what it shows is exactly what is stored.
    /// </summary>
    public static string ValidateChart(string body)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(body, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException error) { throw SprawlException.Invalid($"A chart is a Vega-Lite JSON spec: {error.Message}"); }
        if (node is not JsonObject spec) throw SprawlException.Invalid("A chart is a Vega-Lite JSON object.");
        if (spec["mark"] is null && spec["layer"] is null && spec["hconcat"] is null && spec["vconcat"] is null && spec["concat"] is null
            && spec["facet"] is null && spec["repeat"] is null)
            throw SprawlException.Invalid("A Vega-Lite spec needs a mark (or layer, concat, facet or repeat).");
        if (FindUrl(spec) is { } path) throw SprawlException.Invalid($"Charts take their data inline (data.values); '{path}' points at a URL.");
        return body;
    }

    private static string? FindUrl(JsonNode? node, string path = "")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key.Equals("url", StringComparison.OrdinalIgnoreCase)) return path + key;
                    if (FindUrl(value, path + key + ".") is { } found) return found;
                }
                return null;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    if (FindUrl(array[i], $"{path}{i}.") is { } found) return found;
                return null;
            default:
                return null;
        }
    }

    private static int Round(int value) => (value + 7) / 8 * 8;
}
