using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sprawl.Api.Auth;
using Sprawl.Api.Data;
using Sprawl.Api.Events;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Endpoints;

public static class CanvasEndpoints
{
    public static void MapCanvases(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(SprawlClaims.ApiPolicy);

        api.MapGet("/canvases", (CanvasStore store, string? tag, string? collection, bool? archived, string? q, int? limit, bool? trash, HttpContext context, CancellationToken ct) =>
        {
            if (trash == true && ActorOf(context).Kind != ActorKinds.Owner) throw SprawlException.Forbidden("Only the owner sees the trash.");
            return store.List(new CanvasFilter(tag, collection, archived ?? false, q, trash ?? false, limit ?? 50), ct);
        });

        api.MapPost("/canvases", async (CreateCanvasRequest request, CanvasWriter writer, HttpContext context, CancellationToken ct) =>
        {
            var result = await writer.Create(request, ActorOf(context), ct);
            context.Response.Headers.ETag = Http.ETag(result.Version);
            return TypedResults.Created($"/api/v1/canvases/{result.CanvasId}", result);
        });

        api.MapGet("/canvases/{id}", async (string id, CanvasStore store, HttpContext context, CancellationToken ct) =>
        {
            var document = await store.Document(id, ct);
            return Http.WithVersion(context, document.Version, document);
        });

        api.MapGet("/canvases/{id}/outline", async (string id, CanvasStore store, HttpContext context, string? depth, string? region, bool? connections, int? maxCards, CancellationToken ct) =>
        {
            var (version, outline) = await store.Outline(id, new OutlineOptions(depth ?? OutlineDepths.Short, region, connections ?? true, maxCards ?? 400), ct);
            context.Response.Headers.ETag = Http.ETag(version);
            return Results.Text(outline, "text/plain; charset=utf-8");
        });

        api.MapGet("/canvases/{id}/insights", async (string id, CanvasStore store, CrossLinks cross, HttpContext context, CancellationToken ct) =>
        {
            var (row, state) = await store.Current(id, ct);
            var insights = new Insights(state).Analyze(id, row.Version);
            // A [[Canvas/Card]] link that reaches another canvas is not dangling.
            var reachable = await cross.Reachable(id, insights.DanglingLinks.Select(d => d.Target), ct);
            return Http.WithVersion(context, row.Version, insights with { DanglingLinks = [.. insights.DanglingLinks.Where(d => !reachable.Contains(d.Target))] });
        });

        api.MapGet("/canvases/{id}/links/resolve", async (string id, string target, CrossLinks cross, CancellationToken ct) =>
            await cross.Resolve(id, target, ct) is { } found ? Results.Ok(found) : throw SprawlException.NotFound($"No card matches [[{target}]]."));

        api.MapGet("/canvases/{id}/cards/{cardId}/context", async (string id, string cardId, CanvasStore store, CrossLinks cross, HttpContext context, CancellationToken ct) =>
        {
            var (row, state) = await store.Current(id, ct);
            var card = state.Cards.GetValueOrDefault(cardId) ?? throw SprawlException.NotFound($"Card {cardId} is not on canvas {id}.");
            var local = new Insights(state).Context(id, row.Version, card);
            return Http.WithVersion(context, row.Version, await cross.Extend(local, card, state, ct));
        });

        api.MapGet("/canvases/{id}/export", async (string id, string? format, CanvasStore store, CancellationToken ct) =>
        {
            var (row, state) = await store.Current(id, ct);
            var name = FileName(row.Meta.Title);
            return format switch
            {
                null or "markdown" or "md" => Results.File(Encoding.UTF8.GetBytes(Exchange.Markdown(state)), "text/markdown; charset=utf-8", name + ".md"),
                "json-canvas" or "canvas" => Results.File(JsonSerializer.SerializeToUtf8Bytes(Exchange.ToJsonCanvas(state), SprawlJson.Options), "application/json", name + ".canvas"),
                _ => throw SprawlException.Invalid("format must be markdown or json-canvas.")
            };
        });

        // Owner only: an import is one create batch that can hold a whole canvas, so it is larger than any batch Claude may send.
        api.MapPost("/canvases/import", async (ImportCanvasRequest request, CanvasWriter writer, HttpContext context, CancellationToken ct) =>
        {
            var warnings = new List<string>();
            var intents = Exchange.FromJsonCanvas(request.Canvas, warnings);
            var result = await writer.Create(new CreateCanvasRequest(request.Title, request.Description, Intents: intents, RequestId: request.RequestId, OnDuplicate: DuplicatePolicies.Add),
                ActorOf(context), ct, Exchange.MaxImportIntents);
            context.Response.Headers.ETag = Http.ETag(result.Version);
            return TypedResults.Created($"/api/v1/canvases/{result.CanvasId}", result with { Warnings = [.. warnings, .. result.Warnings] });
        }).RequireAuthorization(SprawlClaims.OwnerPolicy).WithMetadata(new RequestSizeLimitAttribute(ImportBodyLimit));

        api.MapPatch("/canvases/{id}", async (string id, UpdateCanvasIntent update, CanvasWriter writer, HttpContext context, CancellationToken ct) =>
            Result(context, await writer.Batch(id, new BatchRequest([update]), Http.IfMatch(context.Request), ActorOf(context), ct)));

        api.MapDelete("/canvases/{id}", async (string id, CanvasWriter writer, HttpContext context, CancellationToken ct) =>
            Result(context, await writer.Trash(id, Http.IfMatch(context.Request, required: true)!.Value, ActorOf(context), ct)));

        api.MapPost("/canvases/{id}/restore", async (string id, CanvasWriter writer, HttpContext context, CancellationToken ct) =>
            Result(context, await writer.Restore(id, ActorOf(context), ct))).RequireAuthorization(SprawlClaims.OwnerPolicy);

        api.MapDelete("/canvases/{id}/purge", async (string id, CanvasWriter writer, CancellationToken ct) =>
        {
            await writer.Purge(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(SprawlClaims.OwnerPolicy);

        api.MapPost("/canvases/{id}/batches", async (string id, BatchRequest request, CanvasWriter writer, HttpContext context, CancellationToken ct) =>
            Result(context, await writer.Batch(id, request, Http.IfMatch(context.Request), ActorOf(context), ct)));

        api.MapPost("/canvases/{id}/batches/{version:long}/undo", async (string id, long version, CanvasWriter writer, HttpContext context, CancellationToken ct) =>
            Result(context, await writer.Undo(id, version, Http.IfMatch(context.Request), ActorOf(context), ct)));

        api.MapPost("/canvases/{id}/revert", async (string id, RevertRequest request, CanvasWriter writer, HttpContext context, CancellationToken ct) =>
            Result(context, await writer.Revert(id, request.ToVersion, Http.IfMatch(context.Request, required: true)!.Value, ActorOf(context), ct)));

        api.MapGet("/canvases/{id}/history", (string id, long? before, int? limit, CanvasStore store, CancellationToken ct) => store.History(id, before, limit ?? 50, ct));
        api.MapGet("/canvases/{id}/history/{version:long}", (string id, long version, CanvasStore store, CancellationToken ct) => store.Batch(id, version, ct));
        api.MapGet("/canvases/{id}/state", (string id, long version, CanvasStore store, CancellationToken ct) => store.StateAt(id, version, ct));

        api.MapPost("/cards/query", (CardsQuery query, CanvasStore store, CancellationToken ct) => store.Cards(query.Ids, ct));

        api.MapGet("/search", (string q, string? canvas, string? kinds, int? limit, CanvasStore store, CancellationToken ct) =>
            store.Search(q, canvas, kinds?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [], limit ?? 20, ct));

        // Open questions due for review. Claude may read the queue (to research them); only the owner grades.
        api.MapGet("/reviews", (string? canvas, int? limit, ReviewStore reviews, CancellationToken ct) => reviews.Due(canvas, limit ?? 20, ct));
        api.MapPost("/reviews/{cardId}", (string cardId, ReviewRequest request, ReviewStore reviews, CancellationToken ct) => reviews.Grade(cardId, request.Grade, ct))
            .RequireAuthorization(SprawlClaims.OwnerPolicy);

        api.MapGet("/canvases/{id}/events", Stream).RequireAuthorization(SprawlClaims.OwnerPolicy);

        // Owner only: a verified copy now (before an upgrade or a restore drill), in addition to the nightly one.
        api.MapPost("/backups", async (Services.SnapshotBackup backup, CancellationToken ct) =>
        {
            if (backup.Directory is null) throw new SprawlException(ErrorCodes.Conflict, "Backups are not configured (Api:Backup:Directory).", 409);
            var path = await backup.RunOnce(ct);
            return Results.Ok(new { file = Path.GetFileName(path) });
        }).RequireAuthorization(SprawlClaims.OwnerPolicy);
    }

    public const long ImportBodyLimit = 8 * 1024 * 1024;

    /// <summary>A download name from a canvas title: letters, digits and dashes only, never empty.</summary>
    public static string FileName(string title)
    {
        var slug = new string([.. title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]);
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        return slug.Length == 0 ? "canvas" : slug[..Math.Min(slug.Length, 60)].TrimEnd('-');
    }

    private static IResult Result(HttpContext context, BatchResult result)
    {
        context.Response.Headers.ETag = Http.ETag(result.Version);
        return TypedResults.Ok(result);
    }

    /// <summary>The acting principal. The client label is accepted only from the MCP device principal, and only as a label.</summary>
    public static Actor ActorOf(HttpContext context)
    {
        var kind = context.User.FindFirstValue(SprawlClaims.Actor);
        if (kind == ActorKinds.Owner) return Actor.Owner;
        if (kind != ActorKinds.Mcp) throw new SprawlException(ErrorCodes.Unauthorized, "Sign in first.", 401);
        var client = context.Request.Headers["X-Sprawl-Client"].ToString();
        return new Actor(ActorKinds.Mcp, client.Length is > 0 and <= 200 && !client.Any(char.IsControl) ? client : null);
    }

    /// <summary>
    /// Server-sent events for one canvas. Subscribes before reading the current version so nothing committed in between
    /// is lost; replays batches after <c>Last-Event-ID</c> (or <c>?since=</c>); a 25 s comment keeps proxies from idling out.
    /// </summary>
    private static async Task Stream(string id, HttpContext context, CanvasStore store, CanvasEventHub hub, long? since, CancellationToken ct)
    {
        using var subscription = hub.Subscribe(id);
        var row = await store.Row(id, ct);
        var lastEventId = context.Request.Headers["Last-Event-ID"].ToString();
        var from = long.TryParse(lastEventId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : since ?? row.Version;
        var response = context.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Accel-Buffering"] = "no";
        await response.WriteAsync($"retry: 3000\n: connected v{row.Version}\n\n", ct);
        await response.Body.FlushAsync(ct);

        var last = from;
        if (from < row.Version)
        {
            var missed = await store.EventsSince(id, from, 500, ct);
            if (missed is null)
            {
                await response.WriteAsync($"event: reset\ndata: {{\"version\":{row.Version}}}\n\n", ct);
                last = row.Version;
            }
            else
                foreach (var canvasEvent in missed) last = await Send(response, canvasEvent, ct);
            await response.Body.FlushAsync(ct);
        }

        Task<CanvasEvent>? pending = null;
        while (!ct.IsCancellationRequested)
        {
            pending ??= subscription.Reader.ReadAsync(ct).AsTask();
            var winner = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(25), ct));
            if (ct.IsCancellationRequested) break;
            if (winner != pending)
            {
                await response.WriteAsync(": ping\n\n", ct);
                await response.Body.FlushAsync(ct);
                continue;
            }
            CanvasEvent next;
            try { next = await pending; }
            catch (System.Threading.Channels.ChannelClosedException) { break; }
            pending = null;
            if (next.Version <= last) continue;
            last = await Send(response, next, ct);
            await response.Body.FlushAsync(ct);
        }
    }

    private static async Task<long> Send(HttpResponse response, CanvasEvent canvasEvent, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(canvasEvent, SprawlJson.Options);
        await response.WriteAsync($"id: {canvasEvent.Version}\nevent: batch\ndata: {json}\n\n", ct);
        return canvasEvent.Version;
    }
}
