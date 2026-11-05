using System.Text.RegularExpressions;

namespace Sprawl.Domain;

/// <summary>
/// A first size for a rendered Mermaid card, estimated from its source so a diagram lands roughly the right shape before
/// the browser draws it (the renderer then scales the drawing to fit; the owner can resize). Sizes are multiples of 8.
/// </summary>
public static partial class MermaidGeometry
{
    public static (int W, int H) Size(string source)
    {
        string type;
        try { type = Mermaid.DiagramType(source); }
        catch (SprawlException) { return (640, 360); }
        var lines = source.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("%%", StringComparison.Ordinal)).Skip(1).ToList();
        var count = lines.Count;
        var (w, h) = type switch
        {
            "sequenceDiagram" or "zenuml" => SequenceSize(lines),
            "mindmap" => (Clamp(Depth(lines) * 200 + 240, 480, 1600), Clamp(count * 34 + 80, 280, 1800)),
            "timeline" => (Clamp(lines.Count(l => l.Contains(':')) * 180 + 120, 480, 1800), 400),
            "gantt" => (960, Clamp(lines.Count(l => l.Contains(':')) * 34 + 140, 240, 1800)),
            "journey" => (Clamp(lines.Count(l => l.Contains(':')) * 120 + 200, 560, 1800), 480),
            "pie" or "radar" or "radar-beta" or "venn" or "venn-beta" => (520, 440),
            "quadrantChart" => (560, 560),
            "xychart" or "xychart-beta" or "sankey" or "sankey-beta" => (760, 480),
            "gitGraph" => (Clamp(count * 60 + 160, 480, 1800), 320),
            "kanban" => (Clamp(lines.Count(l => !char.IsWhiteSpace(l[0])) * 220 + 80, 480, 1800), Clamp(count * 36 + 120, 320, 1400)),
            "erDiagram" or "classDiagram" or "classDiagram-v2" => (Clamp(Entities(lines) * 180 + 200, 480, 1600), Clamp(count * 26 + 160, 320, 1800)),
            _ => (Clamp(400 + count * 12, 480, 1200), Clamp(count * 34 + 120, 300, 1600))
        };
        return (Round(w), Round(h));
    }

    private static (int, int) SequenceSize(List<string> lines)
    {
        var participants = new HashSet<string>(StringComparer.Ordinal);
        var messages = 0;
        foreach (var line in lines)
        {
            var declared = Participant().Match(line);
            if (declared.Success) { participants.Add(declared.Groups[1].Value); continue; }
            var message = Message().Match(line);
            if (!message.Success) continue;
            messages++;
            participants.Add(message.Groups[1].Value);
            participants.Add(message.Groups[2].Value);
        }
        return (Clamp(participants.Count * 170 + 80, 360, 1800), Clamp(messages * 46 + 180, 240, 2200));
    }

    private static int Depth(List<string> lines) =>
        lines.Count == 0 ? 1 : lines.Select(l => l.Length - l.TrimStart().Length).Distinct().Count();

    private static int Entities(List<string> lines) => lines.Count(l => l.TrimEnd().EndsWith('{'));

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    private static int Round(int value) => (value + 7) / 8 * 8;

    [GeneratedRegex(@"^\s*(?:participant|actor)\s+([^\s]+)")] private static partial Regex Participant();
    [GeneratedRegex(@"^\s*([^\s\-<>+:]+)\s*(?:<<)?-{1,2}(?:>>|>|x|\)|\))[+-]?\s*([^\s:]+)\s*:")] private static partial Regex Message();
}
