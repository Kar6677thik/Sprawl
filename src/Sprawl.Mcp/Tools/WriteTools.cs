using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Sprawl.Contracts;
using Sprawl.Mcp.Clients;
using Sprawl.Mcp.Security;
using Sprawl.Mcp.Services;

namespace Sprawl.Mcp.Tools;

/// <summary>What a write returns: the new version (pass it as expectedVersion next time), temp ref → id, and what was skipped.</summary>
public sealed record WriteResult(string CanvasId, long Version, bool Changed, string Summary, IReadOnlyDictionary<string, string> Refs,
    IReadOnlyList<SkippedItem> Skipped, IReadOnlyList<string> Warnings, bool Replayed)
{
    public static WriteResult From(BatchResult r) => new(r.CanvasId, r.Version, r.Changed, r.Summary, r.Refs, r.Skipped, r.Warnings, r.Replayed);
}

internal static class Batches
{
    public const int MaxItems = 100;
}

[McpServerToolType]
[Authorize]
public sealed class WriteTools(SprawlApiClient api, ToolExecutor executor)
{
    [McpServerTool(Name = "create_canvas", Title = "Create a canvas", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Write)]
    [Authorize(Policy = "scope:canvas.write")]
    [Description("Create a canvas for a new topic, ideally seeded in the same call with 3-6 regions and their first cards (plus open questions). " +
        "Call list_canvases first: if a canvas for the topic exists, add to it instead. The server places everything; never think in coordinates.")]
    public Task<McpResult<WriteResult>> CreateCanvas(
        [Description("The topic, 1-120 characters.")] string title,
        [Description("One or two sentences on what this canvas maps.")] string? description = null,
        [Description("Up to 12 tags, e.g. distributed-systems.")] IReadOnlyList<string>? tags = null,
        [Description("Optional folder-like collection name.")] string? collection = null,
        [Description("Regions to create.")] IReadOnlyList<RegionInput>? regions = null,
        [Description("Cards to create; each names its region (a ref from this call, or a title).")] IReadOnlyList<CardInput>? cards = null,
        [Description("Connections between refs from this call.")] IReadOnlyList<ConnectionInput>? connections = null,
        [Description("Any unique string; if this call is retried with the same requestId, the first result is returned instead of creating a second canvas.")] string? requestId = null,
        CancellationToken token = default) =>
        executor.Run("create_canvas", async () =>
        {
            var intents = Map.Additions(regions, cards, connections).ToList();
            if (intents.Count > Batches.MaxItems) throw new ArgumentException($"At most {Batches.MaxItems} regions, cards and connections per call; add the rest with add_to_canvas.");
            return WriteResult.From(await api.Create(new CreateCanvasRequest(title, description, tags, collection, intents, requestId), token));
        }, token);

    [McpServerTool(Name = "add_to_canvas", Title = "Add to a canvas", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Write)]
    [Authorize(Policy = "scope:canvas.write")]
    [Description("Add regions, cards and connections to a canvas in one atomic batch (all or nothing, at most 100 items). " +
        "Read the canvas with get_canvas first to avoid duplicates and to reuse existing region titles (if you read only one region, search for your titles too). " +
        "Give new items a ref (e.g. \"c1\") to connect them in the same call; " +
        "connect to existing cards by id. A card whose title already exists in the same region is skipped (reported in skipped, and its ref points at the existing card); " +
        "one matching a card in another region is added and reported in warnings. " +
        "Prefer one idea per card and use question cards for gaps you cannot fill. Never think in coordinates: the server places everything and never moves what the owner placed.")]
    public Task<McpResult<WriteResult>> AddToCanvas(
        [Description("The canvas id (cv_…).")] string canvasId,
        IReadOnlyList<RegionInput>? regions = null,
        IReadOnlyList<CardInput>? cards = null,
        IReadOnlyList<ConnectionInput>? connections = null,
        [Description("Create a region when a card names one that does not exist (default true).")] bool createMissingRegions = true,
        [Description("skip (default), add anyway, or fail the whole batch, when a card title already exists in its region.")] DuplicatePolicy onDuplicate = DuplicatePolicy.Skip,
        [Description("The version from your last read; if the canvas changed since, nothing is applied and you get stale_version. Optional for pure additions.")] long? expectedVersion = null,
        [Description("Any unique string; a retried call with the same requestId is not applied twice.")] string? requestId = null,
        CancellationToken token = default) =>
        executor.Run("add_to_canvas", async () =>
        {
            var intents = Map.Additions(regions, cards, connections).ToList();
            Map.Count(intents.Count, Batches.MaxItems, "region, card or connection");
            return WriteResult.From(await api.Batch(canvasId, new BatchRequest(intents, requestId, CreateMissingRegions: createMissingRegions, OnDuplicate: Map.Duplicate(onDuplicate)), expectedVersion, token));
        }, token);

    [McpServerTool(Name = "update_cards", Title = "Update cards", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Write)]
    [Authorize(Policy = "scope:canvas.write")]
    [Description("Improve or correct existing cards: title, body (replace, or appendBody to add), kind, URL, language, emphasis, shape. Atomic, at most 50 cards. " +
        "Read them first (get_canvas at depth=full, or get_cards) and pass expectedVersion so you never overwrite a change you have not seen. Changes are undoable by the owner.")]
    public Task<McpResult<WriteResult>> UpdateCards(
        [Description("The canvas id (cv_…).")] string canvasId,
        [Description("1-50 updates; only the fields you give change.")] IReadOnlyList<CardUpdate> updates,
        [Description("The version from your last read.")] long? expectedVersion = null,
        CancellationToken token = default) =>
        executor.Run("update_cards", async () =>
        {
            Map.Count(updates.Count, 50, "update");
            var intents = updates.Select(u => (Intent)new UpdateCardIntent(u.CardId, u.Title, u.Body, u.AppendBody, u.Kind is null ? null : Map.Kind(u.Kind.Value),
                u.Url, u.Language, Map.Emphasis(u.Emphasis), Map.Tint(u.Tint), Map.Shape(u.Shape))).ToList();
            return WriteResult.From(await api.Batch(canvasId, new BatchRequest(intents), expectedVersion, token));
        }, token);

    [McpServerTool(Name = "connect", Title = "Connect cards", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Write)]
    [Authorize(Policy = "scope:canvas.write")]
    [Description("Add connections between existing cards or regions (by id). Connecting a pair that is already connected with the same kind changes nothing.")]
    public Task<McpResult<WriteResult>> Connect(
        [Description("The canvas id (cv_…).")] string canvasId,
        [Description("1-100 connections; from and to are card or region ids.")] IReadOnlyList<ConnectionInput> connections,
        long? expectedVersion = null,
        CancellationToken token = default) =>
        executor.Run("connect", async () =>
        {
            Map.Count(connections.Count, Batches.MaxItems, "connection");
            return WriteResult.From(await api.Batch(canvasId, new BatchRequest([.. Map.Additions(null, null, connections)]), expectedVersion, token));
        }, token);

    [McpServerTool(Name = "disconnect", Title = "Remove connections", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Write)]
    [Authorize(Policy = "scope:canvas.write")]
    [Description("Remove connections, by connection id (cx_…, shown in get_canvas) or by endpoints. Removing one that does not exist is reported as skipped.")]
    public Task<McpResult<WriteResult>> Disconnect(
        [Description("The canvas id (cv_…).")] string canvasId,
        IReadOnlyList<string>? connectionIds = null,
        [Description("Endpoint pairs; omit kind to remove every kind between them.")] IReadOnlyList<ConnectionPair>? pairs = null,
        long? expectedVersion = null,
        CancellationToken token = default) =>
        executor.Run("disconnect", async () =>
        {
            var intents = (connectionIds ?? []).Select(id => (Intent)new DisconnectIntent(id))
                .Concat((pairs ?? []).Select(p => new DisconnectIntent(null, p.From, p.To, p.Kind is null ? null : Map.Kind(p.Kind.Value)))).ToList();
            Map.Count(intents.Count, Batches.MaxItems, "connection");
            return WriteResult.From(await api.Batch(canvasId, new BatchRequest(intents), expectedVersion, token));
        }, token);
}
