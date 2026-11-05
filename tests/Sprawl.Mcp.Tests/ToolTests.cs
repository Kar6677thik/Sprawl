using System.Net;
using System.Text.Json;

namespace Sprawl.Mcp.Tests;

public sealed class ToolTests(McpHost host) : IClassFixture<McpHost>
{
    private static readonly string Everyday = McpHost.Token();
    private static readonly string WithAdmin = McpHost.Token("canvas.read canvas.write canvas.organize canvas.admin");

    [Fact]
    public async Task Every_tool_declares_annotations_scopes_and_a_when_to_use_description()
    {
        var (_, body, _) = await Rpc.Send(host.Client(), WithAdmin, "tools/list");
        var tools = body.GetProperty("result").GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
        var expected = new Dictionary<string, (bool ReadOnly, bool Destructive, bool Idempotent, string Scope)>
        {
            ["list_canvases"] = (true, false, true, "canvas.read"),
            ["search"] = (true, false, true, "canvas.read"),
            ["get_canvas"] = (true, false, true, "canvas.read"),
            ["get_cards"] = (true, false, true, "canvas.read"),
            ["get_history"] = (true, false, true, "canvas.read"),
            ["get_card_context"] = (true, false, true, "canvas.read"),
            ["analyze_canvas"] = (true, false, true, "canvas.read"),
            ["get_due_questions"] = (true, false, true, "canvas.read"),
            ["get_diagram"] = (true, false, true, "canvas.read"),
            ["show_diagram"] = (true, false, true, "canvas.read"),
            ["add_diagram"] = (false, false, false, "canvas.write"),
            ["add_walkthrough"] = (false, false, false, "canvas.write"),
            ["update_connections"] = (false, false, true, "canvas.write"),
            ["create_canvas"] = (false, false, false, "canvas.write"),
            ["add_to_canvas"] = (false, false, false, "canvas.write"),
            ["update_cards"] = (false, true, true, "canvas.write"),
            ["connect"] = (false, false, true, "canvas.write"),
            ["disconnect"] = (false, true, true, "canvas.write"),
            ["reorganize"] = (false, true, false, "canvas.organize"),
            ["update_canvas"] = (false, false, true, "canvas.organize"),
            ["revert_canvas"] = (false, true, true, "canvas.organize"),
            ["delete_cards"] = (false, true, true, "canvas.admin"),
            ["delete_canvas"] = (false, true, true, "canvas.admin"),
        };
        Assert.Equal(expected.Keys.Order(), tools.Keys.Order());
        foreach (var (name, (readOnly, destructive, idempotent, scope)) in expected)
        {
            var tool = tools[name];
            var annotations = tool.GetProperty("annotations");
            Assert.Equal(readOnly, annotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.Equal(destructive, annotations.GetProperty("destructiveHint").GetBoolean());
            Assert.Equal(idempotent, annotations.GetProperty("idempotentHint").GetBoolean());
            Assert.False(annotations.GetProperty("openWorldHint").GetBoolean());
            Assert.Equal(scope, tool.GetProperty("_meta").GetProperty("securitySchemes")[0].GetProperty("scopes")[0].GetString());
            Assert.True(tool.GetProperty("description").GetString()!.Length > 60, name);
        }
        // Coordinates are never part of any input schema: Claude cannot place things by position.
        Assert.DoesNotContain("\"x\"", body.ToString());
        Assert.Contains("\"enum\":[\"idea\",\"note\",\"question\",\"link\",\"code\",\"mermaid\",\"table\",\"chart\",\"widget\"]", body.ToString().Replace(" ", ""));
        Assert.Contains("get_canvas", tools["add_to_canvas"].GetProperty("description").GetString());
    }

    [Fact]
    public async Task Discovery_carries_server_instructions_and_the_prompts()
    {
        var (_, prompts, _) = await Rpc.Send(host.Client(), Everyday, "prompts/list");
        var names = prompts.GetProperty("result").GetProperty("prompts").EnumerateArray().Select(p => p.GetProperty("name").GetString()).Order();
        Assert.Equal(["answer_questions", "expand_region", "find_gaps", "quiz_me", "review_canvas", "seed_canvas", "synthesize_region", "teach_me", "triage_inbox", "visualize"], names);
        var (_, prompt, _) = await Rpc.Send(host.Client(), Everyday, "prompts/get", new Dictionary<string, object?>
        {
            ["name"] = "expand_region",
            ["arguments"] = new Dictionary<string, object?> { ["canvas"] = "Raft", ["region"] = "Safety" }
        });
        var text = prompt.GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString();
        Assert.Contains("get_canvas with region=\"Safety\"", text);
    }

    [Fact]
    public async Task A_full_session_creates_reads_expands_edits_reorganizes_and_reverts_a_canvas()
    {
        var client = host.Client();

        // Create, seeded in one call.
        var created = Rpc.Data(await Rpc.Call(client, Everyday, "create_canvas", new
        {
            title = "CRDTs",
            description = "Conflict-free replicated data types.",
            tags = new[] { "distributed-systems" },
            regions = new object[] { new { title = "State-based", @ref = "state" }, new { title = "Operation-based", @ref = "ops" } },
            cards = new object[]
            {
                new { region = "state", kind = "idea", title = "Merge is a join on a semilattice", body = "Commutative, associative, idempotent.", @ref = "join" },
                new { region = "ops", kind = "question", title = "Do op-based CRDTs need causal delivery?", @ref = "causal" },
            },
            connections = new object[] { new { from = "join", to = "causal", kind = "relates" } },
            requestId = "create-crdts-1"
        }));
        var canvasId = created.GetProperty("canvasId").GetString()!;
        Assert.Equal(1, created.GetProperty("version").GetInt64());
        var join = created.GetProperty("refs").GetProperty("join").GetString()!;

        // A retried create with the same requestId does not make a second canvas.
        var retried = Rpc.Data(await Rpc.Call(client, Everyday, "create_canvas", new { title = "CRDTs", requestId = "create-crdts-1" }));
        Assert.Equal(canvasId, retried.GetProperty("canvasId").GetString());
        Assert.True(retried.GetProperty("replayed").GetBoolean());

        // Read the outline: ids, kinds, titles; never coordinates.
        var outline = Rpc.Text(await Rpc.Call(client, Everyday, "get_canvas", new { canvasId }));
        Assert.Contains($"# CRDTs  {canvasId}  v1", outline);
        Assert.Contains($"- {join} idea Merge is a join on a semilattice — Commutative, associative, idempotent.", outline);
        Assert.Contains("? Do op-based CRDTs need causal delivery?", outline);
        Assert.DoesNotMatch(@"\b(x|y)\s*[:=]\s*\d", outline);

        // Expand: a duplicate title is skipped and its ref resolves to the existing card, so the connection still lands.
        var added = Rpc.Data(await Rpc.Call(client, Everyday, "add_to_canvas", new
        {
            canvasId,
            cards = new object[]
            {
                new { region = "State-based", kind = "note", title = "merge is a join on a semilattice.", @ref = "dup" },
                new { region = "Delta-state", kind = "idea", title = "Ship deltas, not whole states", @ref = "delta" },
            },
            connections = new object[] { new { from = "delta", to = "dup", kind = "depends-on" } },
            expectedVersion = 1
        }));
        Assert.Equal(2, added.GetProperty("version").GetInt64());
        Assert.Equal(join, added.GetProperty("refs").GetProperty("dup").GetString());
        Assert.Equal("duplicate", added.GetProperty("skipped")[0].GetProperty("reason").GetString());

        // A stale expectedVersion changes nothing and says what the version is now.
        var stale = await Rpc.Call(client, Everyday, "update_cards", new { canvasId, updates = new[] { new { cardId = join, title = "Joins" } }, expectedVersion = 1 });
        var error = stale.GetProperty("structuredContent");
        Assert.False(error.GetProperty("ok").GetBoolean());
        Assert.Equal("stale_version", error.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, error.GetProperty("error").GetProperty("currentVersion").GetInt64());

        Rpc.Data(await Rpc.Call(client, Everyday, "update_cards", new { canvasId, updates = new[] { new { cardId = join, appendBody = "So replicas converge." } }, expectedVersion = 2 }));
        var full = Rpc.Data(await Rpc.Call(client, Everyday, "get_cards", new { cardIds = new[] { join } }));

        // Text links and analysis: the delta card does not mention the join card yet, so it is a candidate, not a backlink.
        var context = Rpc.Data(await Rpc.Call(client, Everyday, "get_card_context", new { canvasId, cardId = join }));
        Assert.Equal(join, context.GetProperty("card").GetProperty("id").GetString());
        Assert.Equal(0, context.GetProperty("backlinks").GetArrayLength());
        var analysis = Rpc.Data(await Rpc.Call(client, Everyday, "analyze_canvas", new { canvasId }));
        Assert.Equal(3, analysis.GetProperty("cards").GetInt32());
        Assert.Equal(1, analysis.GetProperty("openQuestions").GetArrayLength());
        var due = Rpc.Data(await Rpc.Call(client, Everyday, "get_due_questions", new { canvasId }));
        Assert.Equal(0, due.GetProperty("dueCount").GetInt32()); // a question is first due a day after it is written
        Assert.EndsWith("So replicas converge.", full[0].GetProperty("body").GetString());
        Assert.Equal("State-based", full[0].GetProperty("region").GetString());
        Assert.False(full[0].GetProperty("placedByOwner").GetBoolean());

        // Reorganize: move the delta card into State-based; the emptied region is then deleted in the same batch.
        var delta = added.GetProperty("refs").GetProperty("delta").GetString()!;
        var reorganized = Rpc.Data(await Rpc.Call(client, Everyday, "reorganize", new
        {
            canvasId,
            expectedVersion = 3,
            actions = new object[]
            {
                new { type = "moveCards", cardIds = new[] { delta }, toRegion = "State-based" },
                new { type = "deleteEmptyRegion", regionId = "Delta-state" },
            }
        }));
        Assert.Equal(4, reorganized.GetProperty("version").GetInt64());
        Assert.DoesNotContain("Delta-state", Rpc.Text(await Rpc.Call(client, Everyday, "get_canvas", new { canvasId, depth = "regions" })));

        // History names the client, and search finds the moved card.
        var history = Rpc.Data(await Rpc.Call(client, Everyday, "get_history", new { canvasId }));
        Assert.Equal(4, history.GetArrayLength());
        Assert.All(history.EnumerateArray(), entry => Assert.Equal(("mcp", McpHost.ClientId), (entry.GetProperty("actor").GetString(), entry.GetProperty("client").GetString())));
        var hits = Rpc.Data(await Rpc.Call(client, Everyday, "search", new { query = "deltas", canvasId }));
        Assert.Equal(delta, hits.GetProperty("cards")[0].GetProperty("cardId").GetString());

        // Revert to version 2: the reorganization and the append are undone, as a new version 5.
        var reverted = Rpc.Data(await Rpc.Call(client, Everyday, "revert_canvas", new { canvasId, toVersion = 2, expectedVersion = 4 }));
        Assert.Equal(5, reverted.GetProperty("version").GetInt64());
        Assert.Contains("## Delta-state", Rpc.Text(await Rpc.Call(client, Everyday, "get_canvas", new { canvasId })));

        // Metadata, then deletion (admin only; the canvas goes to the trash).
        Rpc.Data(await Rpc.Call(client, Everyday, "update_canvas", new { canvasId, addTags = new[] { "papers" }, pinned = true }));
        var listed = Rpc.Data(await Rpc.Call(client, Everyday, "list_canvases", new { tag = "papers" }));
        Assert.Equal(canvasId, listed[0].GetProperty("id").GetString());
        Assert.True(listed[0].GetProperty("pinned").GetBoolean());

        var deleted = Rpc.Data(await Rpc.Call(client, WithAdmin, "delete_cards", new { canvasId, cardIds = new[] { delta }, expectedVersion = 6 }));
        Assert.Equal("-1 card, -1 connection", deleted.GetProperty("summary").GetString());
        Rpc.Data(await Rpc.Call(client, WithAdmin, "delete_canvas", new { canvasId, expectedVersion = 7 }));
        Assert.Equal(0, Rpc.Data(await Rpc.Call(client, Everyday, "list_canvases", new { tag = "papers" })).GetArrayLength());

        // No token or device secret ever reaches a log line.
        var logs = string.Join("\n", host.Logs);
        Assert.DoesNotContain(ApiHost.DeviceToken, logs);
        Assert.DoesNotContain(Everyday, logs);
        Assert.DoesNotContain("eyJ", logs);
    }

    [Fact]
    public async Task Claude_draws_reads_redraws_and_restyles_a_diagram()
    {
        var client = host.Client();
        var created = Rpc.Data(await Rpc.Call(client, Everyday, "create_canvas", new
        {
            title = "Kafka internals",
            regions = new object[] { new { title = "Concepts", @ref = "c" } },
            cards = new object[] { new { region = "c", kind = "idea", title = "Partitions are the unit of order", @ref = "p" } }
        }));
        var canvasId = created.GetProperty("canvasId").GetString()!;

        var drawn = Rpc.Data(await Rpc.Call(client, Everyday, "add_diagram", new
        {
            canvasId,
            title = "Produce path",
            mermaid = "flowchart LR\n  prod((Producer)) --> broker{{Broker}}\n  broker ==>|append| log[(Partition log)]\n  broker -.->|ack| prod",
            near = "Concepts",
            walkthrough = new object[]
            {
                new { text = "A producer sends records to the broker.", highlight = new[] { "prod", "broker" } },
                new { text = "The broker appends them to the partition log.", highlight = new[] { "Broker", "log" } },
                new { text = "And acknowledges, asynchronously.", highlight = new[] { "broker", "prod" } },
            }
        }));
        var broker = drawn.GetProperty("refs").GetProperty("d:broker").GetString()!;
        Assert.StartsWith("cd_", broker);

        // Shapes, the diagram and its walkthrough show in the outline; the diagram reads back as Mermaid.
        var outline = Rpc.Text(await Rpc.Call(client, Everyday, "get_canvas", new { canvasId }));
        Assert.Contains("diagram, flows right", outline);
        Assert.Contains("[hexagon] Broker", outline);
        Assert.Contains("How it works (3 steps)", outline);

        // A rendered sequence diagram, and a walkthrough added on its own.
        var sequence = Rpc.Data(await Rpc.Call(client, Everyday, "add_diagram", new
        {
            canvasId,
            title = "Produce handshake",
            mermaid = "sequenceDiagram\n  participant P as Producer\n  participant B as Broker\n  P->>B: Produce(records)\n  B-->>P: Ack(offset)"
        }));
        Assert.True(sequence.GetProperty("changed").GetBoolean());
        Assert.StartsWith("sequenceDiagram", Rpc.Text(await Rpc.Call(client, Everyday, "get_diagram", new { canvasId, region = "Produce handshake" })));
        Rpc.Data(await Rpc.Call(client, Everyday, "add_walkthrough", new
        {
            canvasId,
            region = "Produce path",
            title = "Failure modes",
            steps = new[] { new { text = "If the broker is down the producer retries.", highlight = new[] { "Producer" }, marks = new[] { new { target = "Broker", state = "down", note = "unreachable" } } } }
        }));
        var full0 = Rpc.Text(await Rpc.Call(client, Everyday, "get_canvas", new { canvasId, depth = "full" }));
        Assert.Contains($"{{{broker} down \"unreachable\"}}", full0);

        // A causal-loop diagram: variables on a circle, loops found and named; marks in add_diagram use Mermaid node ids.
        Rpc.Data(await Rpc.Call(client, Everyday, "add_diagram", new
        {
            canvasId,
            title = "Consumer lag",
            mermaid = "flowchart LR\n  lag[Lag] -->|+| consumers[Consumers added]\n  consumers -->|- delay| lag\n  lag -->|+| rebalances[Rebalances]\n  rebalances -->|+| lag",
            layout = "loops",
            walkthrough = new object[] { new { text = "Rebalances make lag worse.", marks = new[] { new { target = "rebalances", state = "hot" } } } }
        }));
        var loops = Rpc.Text(await Rpc.Call(client, Everyday, "get_canvas", new { canvasId }));
        Assert.Contains("causal-loop diagram", loops);
        Assert.Contains("~ loop B1 (balancing, with a delay): Lag → Consumers added → Lag", loops);
        Assert.Contains("~ loop R1 (reinforcing): Lag → Rebalances → Lag", loops);
        var mermaid = Rpc.Text(await Rpc.Call(client, Everyday, "get_diagram", new { canvasId, region = "Produce path" }));
        Assert.StartsWith("flowchart LR", mermaid);
        Assert.Contains("==>|\"append\"|", mermaid);

        // A shape added piece by piece, and a connection from an explanatory card into the diagram.
        var version = Rpc.Data(await Rpc.Call(client, Everyday, "list_canvases", new { query = "Kafka" }))[0].GetProperty("version").GetInt64();
        var added = Rpc.Data(await Rpc.Call(client, Everyday, "add_to_canvas", new
        {
            canvasId,
            cards = new object[] { new { region = "Produce path", kind = "note", title = "Follower", shape = "cylinder", @ref = "f" } },
            connections = new object[]
            {
                new { from = broker, to = "f", label = "replicate", line = "dashed" },
                new { from = created.GetProperty("refs").GetProperty("p").GetString()!, to = broker, kind = "example-of", ends = "none" }
            },
            expectedVersion = version
        }));

        // Restyle a connection, then redraw the whole diagram in place.
        var full = Rpc.Data(await Rpc.Call(client, Everyday, "get_cards", new { cardIds = new[] { broker } }));
        var link = full[0].GetProperty("connections").EnumerateArray().First(c => c.TryGetProperty("label", out var l) && l.GetString() == "replicate");
        Rpc.Data(await Rpc.Call(client, Everyday, "update_connections", new
        {
            canvasId,
            updates = new[] { new { connectionId = link.GetProperty("id").GetString()!, line = "thick", ends = "both" } }
        }));
        var redraw = await Rpc.Call(client, Everyday, "add_diagram", new { canvasId, title = "Produce path", mermaid = "flowchart TD\n  a[Producer] --> b[Broker]", replace = true });
        Assert.Equal("invalid_request", redraw.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString()); // replace needs expectedVersion
        var current = Rpc.Data(await Rpc.Call(client, Everyday, "list_canvases", new { query = "Kafka" }))[0].GetProperty("version").GetInt64();
        Rpc.Data(await Rpc.Call(client, Everyday, "add_diagram", new { canvasId, title = "Produce path", mermaid = "flowchart TD\n  a[Producer] --> b[Broker]", replace = true, expectedVersion = current }));
        var after = Rpc.Text(await Rpc.Call(client, Everyday, "get_diagram", new { canvasId, region = "Produce path" }));
        Assert.StartsWith("flowchart TD", after);
        Assert.DoesNotContain("Partition log", after);
        Assert.True(added.GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task Diagrams_show_in_the_conversation_through_an_mcp_app()
    {
        var client = host.Client();
        // The tool points at the app; the app is an HTML resource with the MCP Apps MIME type.
        var tools = (await Rpc.Send(client, Everyday, "tools/list")).Body.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        foreach (var name in new[] { "show_diagram", "add_diagram" })
            Assert.Equal("ui://sprawl/diagram", tools.Single(t => t.GetProperty("name").GetString() == name).GetProperty("_meta").GetProperty("ui").GetProperty("resourceUri").GetString());
        var resources = (await Rpc.Send(client, Everyday, "resources/list")).Body.GetProperty("result").GetProperty("resources").EnumerateArray().ToList();
        Assert.Contains(resources, r => r.GetProperty("uri").GetString() == "ui://sprawl/diagram" && r.GetProperty("mimeType").GetString() == "text/html;profile=mcp-app");
        var read = (await Rpc.Send(client, Everyday, "resources/read", new Dictionary<string, object?> { ["uri"] = "ui://sprawl/diagram" })).Body;
        Assert.True(read.TryGetProperty("result", out var readResult), read.ToString());
        var html = readResult.GetProperty("contents")[0].GetProperty("text").GetString()!;
        Assert.Contains("ui/initialize", html);
        Assert.Contains("ui/notifications/tool-result", html);
        Assert.DoesNotContain("http://", html.Replace("http://www.w3.org", "", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Rpc.Send(client, null, "resources/read", new Dictionary<string, object?> { ["uri"] = "ui://sprawl/diagram" })).Status);

        // show_diagram returns the drawing, the Mermaid and the walkthroughs.
        var created = Rpc.Data(await Rpc.Call(client, Everyday, "create_canvas", new { title = "App check" }));
        var canvasId = created.GetProperty("canvasId").GetString()!;
        Rpc.Data(await Rpc.Call(client, Everyday, "add_diagram", new
        {
            canvasId, title = "Tiny", mermaid = "flowchart LR\n  a[Ask] --> b[Answer]",
            walkthrough = new object[] { new { text = "You ask.", highlight = new[] { "a" } }, new { text = "It answers.", highlight = new[] { "a", "b" } } }
        }));
        var view = Rpc.Data(await Rpc.Call(client, Everyday, "show_diagram", new { canvasId, region = "Tiny" }));
        Assert.StartsWith("<svg", view.GetProperty("svg").GetString());
        Assert.StartsWith("flowchart LR", view.GetProperty("mermaid").GetString());
        Assert.Equal(2, view.GetProperty("walkthroughs")[0].GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task Upstream_refusals_come_back_as_stable_error_codes()
    {
        var client = host.Client();
        var missing = (await Rpc.Call(client, Everyday, "add_to_canvas", new { canvasId = "cv_zzzzzzzz", cards = new[] { new { region = "R", kind = "idea", title = "x" } } }))
            .GetProperty("structuredContent");
        Assert.Equal("not_found", missing.GetProperty("error").GetProperty("code").GetString());

        var empty = (await Rpc.Call(client, Everyday, "add_to_canvas", new { canvasId = "cv_zzzzzzzz" })).GetProperty("structuredContent");
        Assert.Equal("invalid_request", empty.GetProperty("error").GetProperty("code").GetString());

        var outline = await Rpc.Call(client, Everyday, "get_canvas", new { canvasId = "cv_zzzzzzzz" });
        Assert.True(outline.GetProperty("isError").GetBoolean());
        Assert.StartsWith("not_found:", Rpc.Text(outline));
    }
}
