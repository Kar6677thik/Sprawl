using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Sprawl.Mcp.Tests;

/// <summary>
/// Every structured tool result must satisfy the outputSchema the tool declares in tools/list, or strict MCP clients reject the
/// whole result. Exercised the way they fail in practice: optional fields left empty (a canvas in no collection, a card with no
/// url, a configured and an unconfigured web app), success and error envelopes, and every tool that declares a schema.
/// </summary>
public sealed class OutputSchemaTests
{
    private static readonly string Admin = McpHost.Token("canvas.read canvas.write canvas.organize canvas.admin");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_structured_result_satisfies_its_declared_output_schema(bool webApp)
    {
        using var host = new McpHost();
        if (webApp) host.Settings["Mcp:WebAppUrl"] = "https://sprawl.test";
        var client = host.Client();

        var (_, list, _) = await Rpc.Send(client, Admin, "tools/list");
        var schemas = list.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Where(t => t.TryGetProperty("outputSchema", out _))
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => JsonSchema.FromText(t.GetProperty("outputSchema").GetRawText()));
        var seen = new HashSet<string>();
        var failures = new List<string>();

        async Task<JsonElement> Call(string tool, object arguments)
        {
            var result = await Rpc.Call(client, Admin, tool, arguments);
            if (!schemas.TryGetValue(tool, out var schema)) return result;
            seen.Add(tool);
            var structured = result.GetProperty("structuredContent");
            var evaluation = schema.Evaluate(JsonNode.Parse(structured.GetRawText()), new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!evaluation.IsValid)
                failures.Add($"{tool}: " + string.Join("; ", evaluation.Details.Where(d => d.HasErrors)
                    .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} {e.Value}")).Take(5)));
            return structured;
        }
        static JsonElement Data(JsonElement structured) => structured.GetProperty("data");

        // A canvas in no collection, with a plain card, a link card and a connection: the shapes that left fields empty.
        var created = Data(await Call("create_canvas", new
        {
            title = $"Schema check {webApp}",
            regions = new object[] { new { title = "Ideas", @ref = "r" } },
            cards = new object[]
            {
                new { region = "r", kind = "idea", title = "Optimistic concurrency", @ref = "a" },
                new { region = "r", kind = "link", title = "A source", url = "https://example.org/page", @ref = "b" },
                new { region = "r", kind = "question", title = "When does it fail?", @ref = "q" },
            },
            connections = new object[] { new { from = "a", to = "b" } }
        }));
        var canvasId = created.GetProperty("canvasId").GetString()!;
        var refs = created.GetProperty("refs");
        var (a, b, q) = (refs.GetProperty("a").GetString()!, refs.GetProperty("b").GetString()!, refs.GetProperty("q").GetString()!);

        await Call("list_canvases", new { });
        await Call("search", new { query = "optimistic" });
        var cards = Data(await Call("get_cards", new { cardIds = new[] { a, b, q } }));
        var connectionId = cards[0].GetProperty("connections")[0].GetProperty("id").GetString()!;
        await Call("get_card_context", new { canvasId, cardId = a });
        await Call("analyze_canvas", new { canvasId });
        await Call("get_due_questions", new { });
        await Call("get_history", new { canvasId });
        await Call("add_to_canvas", new { canvasId, cards = new object[] { new { region = "Ideas", kind = "note", title = "ETags carry the version" } } });
        await Call("update_cards", new { canvasId, updates = new object[] { new { cardId = a, body = "Check the version on write." } } });
        await Call("connect", new { canvasId, connections = new object[] { new { from = q, to = a } } });
        await Call("update_connections", new { canvasId, updates = new object[] { new { connectionId, label = "cites" } } });
        await Call("disconnect", new { canvasId, connectionIds = new[] { connectionId } });
        await Call("add_diagram", new { canvasId, title = "Write path", mermaid = "flowchart LR\n  c[Client] -->|If-Match| s[(Server)]",
            walkthrough = new object[] { new { text = "The client sends the version.", highlight = new[] { "c" } } } });
        await Call("add_walkthrough", new { canvasId, region = "Write path", title = "Conflict", steps = new object[] { new { text = "Stale write.", marks = new object[] { new { target = "Server", state = "hot" } } } } });
        await Call("show_diagram", new { canvasId, region = "Write path" });
        var version = Data(await Call("update_canvas", new { canvasId, description = "Checking schemas." })).GetProperty("version").GetInt64();
        version = Data(await Call("reorganize", new { canvasId, expectedVersion = version, actions = new object[] { new { type = "renameRegion", regionId = refs.GetProperty("r").GetString(), title = "Concepts" } } })).GetProperty("version").GetInt64();
        version = Data(await Call("revert_canvas", new { canvasId, toVersion = version - 1, expectedVersion = version })).GetProperty("version").GetInt64();
        version = Data(await Call("delete_cards", new { canvasId, cardIds = new[] { q }, expectedVersion = version })).GetProperty("version").GetInt64();

        // Error envelopes are results too: data is null and error is set.
        var failed = await Call("update_cards", new { canvasId, updates = new object[] { new { cardId = "cd_missing0", title = "x" } } });
        Assert.False(failed.GetProperty("ok").GetBoolean());
        var stale = await Call("delete_canvas", new { canvasId, expectedVersion = 1 });
        Assert.False(stale.GetProperty("ok").GetBoolean());
        await Call("delete_canvas", new { canvasId, expectedVersion = version });

        Assert.True(failures.Count == 0, "Results that break their own outputSchema:\n" + string.Join("\n", failures));
        Assert.Empty(schemas.Keys.Except(seen));
    }
}
