using System.Text;
using Sprawl.Api.Auth;
using Sprawl.Api.Data;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Endpoints;

/// <summary>
/// Serves a widget card (an explorable written by Claude) as its own document, for a sandboxed iframe. The document gets
/// a policy of its own: inline script and style may run, but nothing may be fetched, connected to, framed or submitted,
/// and only Sprawl may frame it. The UI frames it with <c>sandbox="allow-scripts"</c> and no <c>allow-same-origin</c>,
/// so the widget runs in an opaque origin: it cannot read Sprawl's cookies, storage or API, whatever it contains.
/// </summary>
public static class Widgets
{
    public const string Policy = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; font-src data:; media-src data: blob:; " +
        "connect-src 'none'; form-action 'none'; base-uri 'none'; frame-src 'none'; worker-src 'none'; frame-ancestors 'self'";

    public static void MapWidgets(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/canvases/{id}/cards/{cardId}/widget", async (string id, string cardId, string? theme, CanvasStore store, HttpContext context, CancellationToken ct) =>
        {
            var (_, state) = await store.Current(id, ct);
            var card = state.Cards.GetValueOrDefault(cardId) ?? throw SprawlException.NotFound($"Card {cardId} is not on canvas {id}.");
            if (card.Kind != CardKinds.Widget) throw SprawlException.Invalid("That card is not a widget.");
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = Policy;
            headers.XFrameOptions = "SAMEORIGIN";
            headers.CacheControl = "no-store";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            return Results.Text(Document(card.Body, theme == "dark"), "text/html; charset=utf-8");
        }).RequireAuthorization(SprawlClaims.OwnerPolicy);
    }

    /// <summary>A full document as written, or a fragment wrapped in a page with Sprawl's type and colours.</summary>
    public static string Document(string body, bool dark)
    {
        if (body.Contains("<html", StringComparison.OrdinalIgnoreCase)) return body;
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"").Append(dark ? " class=\"dark\"" : "").Append("><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("""
            <style>
            :root { color-scheme: light; --bg: oklch(1 0 0); --surface: oklch(0.975 0 0); --text: oklch(0.22 0 0); --muted: oklch(0.48 0 0); --border: oklch(0.9 0 0); --accent: oklch(0.62 0.17 255); }
            :root.dark { color-scheme: dark; --bg: oklch(0.215 0 0); --surface: oklch(0.17 0 0); --text: oklch(0.93 0 0); --muted: oklch(0.7 0 0); --border: oklch(0.3 0 0); --accent: oklch(0.62 0.17 255); }
            html, body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.5 Inter, system-ui, -apple-system, "Segoe UI", sans-serif; }
            body { padding: 12px 14px; box-sizing: border-box; }
            input[type=range] { accent-color: var(--accent); }
            button { font: inherit; color: var(--text); background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 4px 10px; cursor: pointer; }
            </style></head><body>
            """);
        html.Append(body);
        html.Append("</body></html>");
        return html.ToString();
    }
}
