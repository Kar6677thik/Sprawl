using Microsoft.Extensions.Options;
using Sprawl.Mcp.Configuration;

namespace Sprawl.Mcp.Services;

/// <summary>
/// Addresses a person can open in the Sprawl web app: <c>{Mcp:WebAppUrl}/c/{canvas}</c>, and <c>?focus={card or region}</c> to
/// open on one item. Without Mcp:WebAppUrl there are none, and results leave their <c>url</c>/<c>webUrl</c> out. Clients copy these
/// as they are; they never build them. Ids are checked to be plain ids before they go into a URL.
/// </summary>
public sealed class WebLinks(IOptions<SprawlMcpOptions> options)
{
    private readonly string? origin = string.IsNullOrWhiteSpace(options.Value.WebAppUrl) ? null : options.Value.WebAppUrl.Trim().TrimEnd('/');

    public string? Canvas(string? canvasId) => origin is null || !IsId(canvasId) ? null : $"{origin}/c/{canvasId}";

    public string? Item(string? canvasId, string? itemId) =>
        Canvas(canvasId) is { } canvas && IsId(itemId) && (itemId!.StartsWith("cd_", StringComparison.Ordinal) || itemId.StartsWith("rg_", StringComparison.Ordinal))
            ? $"{canvas}?focus={itemId}" : null;

    // Stamping: every card a result hands out carries its own address, built here (the app), never by the client.
    public Sprawl.Contracts.CardRef Card(string canvasId, Sprawl.Contracts.CardRef card) => card with { WebUrl = Item(canvasId, card.Id) };

    public Sprawl.Contracts.ReviewQueue Stamp(Sprawl.Contracts.ReviewQueue queue)
    {
        Sprawl.Contracts.DueQuestion One(Sprawl.Contracts.DueQuestion q) => q with { Card = Card(q.CanvasId, q.Card), CanvasUrl = Canvas(q.CanvasId) };
        return queue with { Due = [.. queue.Due.Select(One)], Upcoming = [.. queue.Upcoming.Select(One)] };
    }

    public Sprawl.Contracts.CardContext Stamp(Sprawl.Contracts.CardContext c)
    {
        Sprawl.Contracts.CardRef Here(Sprawl.Contracts.CardRef card) => Card(c.CanvasId, card);
        Sprawl.Contracts.ExternalCardRef There(Sprawl.Contracts.ExternalCardRef e) => e with { Card = Card(e.CanvasId, e.Card) };
        return c with
        {
            Card = Here(c.Card), Links = [.. c.Links.Select(Here)], Backlinks = [.. c.Backlinks.Select(Here)], Mentions = [.. c.Mentions.Select(Here)],
            Related = [.. c.Related.Select(r => r with { Card = Here(r.Card) })],
            CrossLinks = [.. c.CrossLinks.Select(There)], CrossBacklinks = [.. c.CrossBacklinks.Select(There)]
        };
    }

    public Sprawl.Contracts.CanvasInsights Stamp(Sprawl.Contracts.CanvasInsights i)
    {
        Sprawl.Contracts.CardRef Here(Sprawl.Contracts.CardRef card) => Card(i.CanvasId, card);
        return i with
        {
            Orphans = [.. i.Orphans.Select(Here)], OpenQuestions = [.. i.OpenQuestions.Select(Here)],
            DanglingLinks = [.. i.DanglingLinks.Select(d => d with { From = [.. d.From.Select(Here)] })],
            Hubs = [.. i.Hubs.Select(h => h with { Card = Here(h.Card) })]
        };
    }

    private static bool IsId(string? id) => id is { Length: > 3 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
