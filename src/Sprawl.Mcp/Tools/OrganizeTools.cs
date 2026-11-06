using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Sprawl.Contracts;
using Sprawl.Mcp.Clients;
using Sprawl.Mcp.Security;
using Sprawl.Mcp.Services;

namespace Sprawl.Mcp.Tools;

[McpServerToolType]
[Authorize]
public sealed class OrganizeTools(SprawlApiClient api, ToolExecutor executor, WebLinks links)
{
    [McpServerTool(Name = "reorganize", Title = "Reorganize a canvas", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Organize)]
    [Authorize(Policy = "scope:canvas.organize")]
    [Description("Restructure a canvas in one atomic batch: move cards between regions, create, rename, nest or merge regions, merge duplicate cards, delete empty regions. " +
        "Read the canvas with get_canvas first; expectedVersion is required, so a reorganization is never based on a stale read. " +
        "Cards and regions the owner placed by hand are never moved: such an action fails with pinned_item unless skipPinned is true (then they are reported as skipped). " +
        "Merging cards keeps intoId, appends the others' bodies, rewires their connections and removes them. Everything is undoable by the owner.")]
    public Task<McpResult<WriteResult>> Reorganize(
        [Description("The canvas id (cv_…).")] string canvasId,
        [Description("The version from your get_canvas read.")] long expectedVersion,
        [Description("1-100 actions, applied in order.")] IReadOnlyList<ReorganizeAction> actions,
        [Description("Skip, rather than fail on, items the owner placed by hand.")] bool skipPinned = false,
        CancellationToken token = default) =>
        executor.Run("reorganize", async () =>
        {
            Map.Count(actions.Count, 100, "action");
            var intents = actions.Select(Map.Reorganize).ToList();
            return WriteResult.From(await api.Batch(canvasId, new BatchRequest(intents, SkipPinned: skipPinned, CreateMissingRegions: false), expectedVersion, token), links);
        }, token);

    [McpServerTool(Name = "update_canvas", Title = "Update canvas details", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Organize)]
    [Authorize(Policy = "scope:canvas.organize")]
    [Description("Change a canvas's title, description, tags, collection, or pinned/archived state. Only the fields you give change.")]
    public Task<McpResult<WriteResult>> UpdateCanvas(
        [Description("The canvas id (cv_…).")] string canvasId,
        string? title = null,
        string? description = null,
        [Description("Replace all tags.")] IReadOnlyList<string>? tags = null,
        IReadOnlyList<string>? addTags = null,
        IReadOnlyList<string>? removeTags = null,
        [Description("Move into this collection.")] string? collection = null,
        [Description("Remove it from its collection.")] bool clearCollection = false,
        bool? pinned = null,
        bool? archived = null,
        long? expectedVersion = null,
        CancellationToken token = default) =>
        executor.Run("update_canvas", async () => WriteResult.From(await api.UpdateCanvas(canvasId,
            new UpdateCanvasIntent(title, description, tags, addTags, removeTags, collection, clearCollection, pinned, archived), expectedVersion, token), links), token);

    [McpServerTool(Name = "revert_canvas", Title = "Revert a canvas", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Organize)]
    [Authorize(Policy = "scope:canvas.organize")]
    [Description("Return a canvas to the state it had at an earlier version (see get_history). This adds a new version; nothing is lost, and reverting again to the version before undoes it. " +
        "Use only when the owner asks, or to undo your own mistake.")]
    public Task<McpResult<WriteResult>> RevertCanvas(
        [Description("The canvas id (cv_…).")] string canvasId,
        [Description("The earlier version to return to.")] long toVersion,
        [Description("The current version (from get_canvas or get_history).")] long expectedVersion,
        CancellationToken token = default) =>
        executor.Run("revert_canvas", async () => WriteResult.From(await api.Revert(canvasId, toVersion, expectedVersion, token), links), token);
}

[McpServerToolType]
[Authorize]
public sealed class AdminTools(SprawlApiClient api, ToolExecutor executor, WebLinks links)
{
    [McpServerTool(Name = "delete_cards", Title = "Delete cards", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Admin)]
    [Authorize(Policy = "scope:canvas.admin")]
    [Description("Delete cards and their connections. Only when the owner asks you to delete; to tidy duplicates prefer reorganize with mergeCards. Revertible from history.")]
    public Task<McpResult<WriteResult>> DeleteCards(
        [Description("The canvas id (cv_…).")] string canvasId,
        [Description("1-100 card ids (cd_…).")] IReadOnlyList<string> cardIds,
        [Description("The current version; required.")] long expectedVersion,
        CancellationToken token = default) =>
        executor.Run("delete_cards", async () =>
        {
            Map.Count(cardIds.Count, 100, "card id");
            return WriteResult.From(await api.Batch(canvasId, new BatchRequest([new DeleteCardsIntent(cardIds)]), expectedVersion, token), links);
        }, token);

    [McpServerTool(Name = "delete_canvas", Title = "Delete a canvas", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = ToolSecurity.Admin)]
    [Authorize(Policy = "scope:canvas.admin")]
    [Description("Move a canvas to the trash. The owner can restore it in the Sprawl UI for 30 days; this tool can never delete permanently. Only when the owner asks.")]
    public Task<McpResult<WriteResult>> DeleteCanvas(
        [Description("The canvas id (cv_…).")] string canvasId,
        [Description("The current version; required.")] long expectedVersion,
        CancellationToken token = default) =>
        executor.Run("delete_canvas", async () => WriteResult.From(await api.Trash(canvasId, expectedVersion, token), links, linked: false), token);
}
