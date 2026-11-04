using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Sprawl.Mcp.Prompts;

/// <summary>User-invoked workflows (slash commands in Claude). They only describe what to do; the tools do it.</summary>
[McpServerPromptType]
[Authorize(Policy = "scope:canvas.read")]
public sealed class CanvasPrompts
{
    [McpServerPrompt(Name = "expand_region", Title = "Expand a region")]
    [Description("Grow one region of a canvas: find what is missing and add it.")]
    public static string ExpandRegion(
        [Description("The canvas id (cv_…) or its title.")] string canvas,
        [Description("The region to expand (title or id).")] string region,
        [Description("Optional angle to focus on.")] string? focus = null) =>
        $"""
        Expand the "{region}" region of the Sprawl canvas "{canvas}"{(string.IsNullOrWhiteSpace(focus) ? "" : $", focusing on {focus}")}.
        1. If you only have a title, call list_canvases to find the canvas id.
        2. Call get_canvas with region="{region}" and depth=full to see exactly what is already there.
        3. Work out what is missing: core ideas, mechanisms, examples, counterpoints, sources. Add 4-10 atomic cards with add_to_canvas
           (one idea per card, short bodies), plus question cards for the open questions you cannot answer.
        4. Connect each new card to the existing card it most relates to (leads-to, depends-on, example-of, contradicts or relates).
        5. Pass the version from step 2 as expectedVersion, and a requestId.
        Do not repeat existing cards, and do not reorganize the region unless asked. Finish with a two-line summary of what you added.
        """;

    [McpServerPrompt(Name = "review_canvas", Title = "Review a canvas")]
    [Description("Find weak, duplicate, unclear or contradictory cards and propose fixes before applying them.")]
    public static string ReviewCanvas([Description("The canvas id (cv_…) or its title.")] string canvas) =>
        $"""
        Review the Sprawl canvas "{canvas}".
        1. Call get_canvas (depth=short; for a large canvas, read depth=regions first and then each region).
        2. Report, briefly: duplicate or overlapping cards, vague or weak cards, factual errors, contradictions that are not marked,
           cards in the wrong region, orphan cards with no connections, and the most important open questions.
        3. Propose concrete fixes: update_cards for rewrites, reorganize (mergeCards, moveCards, mergeRegions) for structure.
        4. Apply nothing until the owner confirms. When they do, pass the version you read as expectedVersion.
        """;

    [McpServerPrompt(Name = "seed_canvas", Title = "Start a canvas")]
    [Description("Create a new canvas for a topic, seeded with its main regions and first cards.")]
    public static string SeedCanvas([Description("The topic to map.")] string topic) =>
        $"""
        Start a Sprawl canvas for "{topic}".
        1. Call list_canvases with query="{topic}". If a matching canvas exists, say so and offer to expand it instead.
        2. Otherwise call create_canvas with a clear title, a one-sentence description, a few tags, 3-6 regions that divide the topic,
           and 2-5 atomic cards per region, including at least one question card per region for what is still open.
        3. Connect the most important relationships across regions.
        Keep bodies short. Finish with the region names and the open questions.
        """;
}
