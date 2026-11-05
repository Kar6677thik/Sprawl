using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sprawl.Contracts;

namespace Sprawl.Api.Tests;

public sealed class CanvasApiTests(SprawlFactory factory) : IClassFixture<SprawlFactory>
{
    private static readonly HttpMethod Post = HttpMethod.Post;

    private async Task<BatchResult> NewCanvas(HttpClient client, string title, params Intent[] intents) =>
        await (await client.PostAsJsonAsync("/api/v1/canvases", new CreateCanvasRequest(title, Intents: intents), Api.Json)).Read<BatchResult>();

    private static AddCardIntent Card(string region, string title, string? reference = null, string kind = CardKinds.Idea, string? body = null) =>
        new(kind, title, region, reference, body);

    [Fact]
    public async Task The_mcp_creates_a_seeded_canvas_and_the_owner_reads_it_with_ai_attribution()
    {
        var device = factory.Device();
        var created = await NewCanvas(device, "Gossip protocols", Card("Basics", "Epidemic spread", "e"), Card("Basics", "Fan-out?", "f", CardKinds.Question),
            new ConnectIntent("e", "f"));
        Assert.Equal(1, created.Version);
        Assert.StartsWith("Created canvas; +2 cards in Basics", created.Summary);

        var owner = await factory.Owner();
        var response = await owner.GetAsync($"/api/v1/canvases/{created.CanvasId}");
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
        var document = await response.Read<CanvasDocument>();
        Assert.Equal(2, document.Cards.Count);
        Assert.All(document.Cards, card => Assert.Equal(Authors.Ai, card.CreatedBy));
        Assert.Single(document.Connections);
        Assert.Empty(document.LayoutWarnings);

        var history = await (await owner.GetAsync($"/api/v1/canvases/{created.CanvasId}/history")).Read<List<HistoryEntry>>();
        var first = Assert.Single(history);
        Assert.Equal((BatchKinds.Create, ActorKinds.Mcp, "https://claude.ai/oauth/test"), (first.Kind, first.Actor, first.Client));
    }

    [Fact]
    public async Task Insights_and_card_context_are_derived_from_text_and_connections()
    {
        var device = factory.Device();
        var created = await NewCanvas(device, "Consensus insights",
            Card("Election", "Election timeouts", "t", body: "Randomized election timeouts avoid split votes. #liveness"),
            Card("Availability", "Pre-vote", "p", body: "Pre-vote stops partitioned nodes disrupting election timeouts. See [[Quorum math]]. #liveness"),
            Card("Availability", "Check-quorum", "q", body: "Leaders step down without [[Pre-vote]] style contact."));
        var t = created.Refs["t"];

        var insights = await (await device.GetAsync($"/api/v1/canvases/{created.CanvasId}/insights")).Read<CanvasInsights>();
        Assert.Equal(3, insights.Cards);
        Assert.Equal(1, insights.TextLinks);
        Assert.Equal("Quorum math", Assert.Single(insights.DanglingLinks).Target);
        Assert.Equal(new TagCount("liveness", 2), Assert.Single(insights.Tags));

        var context = await (await device.GetAsync($"/api/v1/canvases/{created.CanvasId}/cards/{created.Refs["p"]}/context")).Read<CardContext>();
        Assert.Equal(["liveness"], context.Tags);
        Assert.Equal(["Quorum math"], context.UnresolvedLinks);
        Assert.Equal(created.Refs["q"], Assert.Single(context.Backlinks).Id);
        Assert.Contains(context.Related, r => r.Card.Id == t);

        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync($"/api/v1/canvases/{created.CanvasId}/cards/cd_zzzzzzzz/context")).StatusCode);
    }

    [Fact]
    public async Task Canvases_export_as_markdown_and_json_canvas_and_the_owner_imports_large_json_canvases()
    {
        var device = factory.Device();
        var created = await NewCanvas(device, "Export me: Raft!", Card("Basics", "Terms", "t", body: "Logical clocks."), Card("Basics", "Votes?", "v", CardKinds.Question),
            new ConnectIntent("t", "v", ConnectionKinds.LeadsTo));

        var markdown = await device.GetAsync($"/api/v1/canvases/{created.CanvasId}/export?format=markdown");
        Assert.Equal("text/markdown", markdown.Content.Headers.ContentType?.MediaType);
        Assert.Equal("export-me-raft.md", markdown.Content.Headers.ContentDisposition?.FileNameStar ?? markdown.Content.Headers.ContentDisposition?.FileName);
        Assert.StartsWith("# Export me: Raft!\n\n## Basics\n\n- **Terms**\n  Logical clocks.\n", await markdown.Content.ReadAsStringAsync());

        var exported = await (await device.GetAsync($"/api/v1/canvases/{created.CanvasId}/export?format=json-canvas")).Read<JsonCanvasDocument>();
        Assert.Equal(3, exported.Nodes!.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await device.GetAsync($"/api/v1/canvases/{created.CanvasId}/export?format=pdf")).StatusCode);

        // Import is owner-only, and can hold far more than one batch: 400 cards (over 1 MiB of JSON) in one create.
        var nodes = Enumerable.Range(0, 400).Select(i => new JsonCanvasNode($"n{i}", "text", i % 20 * 300, i / 20 * 200, 280, 120, Text: $"Card {i}\n" + new string('x', 2600))).ToList();
        var big = new ImportCanvasRequest("Imported", new JsonCanvasDocument([.. exported.Nodes!, .. nodes], exported.Edges));
        Assert.Equal(HttpStatusCode.Forbidden, (await device.PostAsJsonAsync("/api/v1/canvases/import", big, Api.Json)).StatusCode);
        var owner = await factory.Owner();
        var response = await owner.PostAsJsonAsync("/api/v1/canvases/import", big, Api.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var imported = await (await owner.GetAsync($"/api/v1/canvases/{(await response.Read<BatchResult>()).CanvasId}")).Read<CanvasDocument>();
        Assert.Equal(402, imported.Cards.Count);
        Assert.Contains(imported.Cards, c => c.Title == "Votes?" && c.Kind == CardKinds.Question);
        Assert.Equal(ConnectionKinds.LeadsTo, Assert.Single(imported.Connections).Kind);
    }

    [Fact]
    public async Task Open_questions_come_due_a_day_after_writing_and_grading_reschedules_them()
    {
        var device = factory.Device();
        var created = await NewCanvas(device, "Review queue", Card("Open", "Why is the sky blue?", "q", CardKinds.Question), Card("Open", "Rayleigh", "n"));
        var queue = $"/api/v1/reviews?canvas={created.CanvasId}";
        Assert.Equal(0, (await (await device.GetAsync(queue)).Read<ReviewQueue>()).DueCount);

        factory.Clock.Advance(TimeSpan.FromDays(1.01));
        var due = await (await device.GetAsync(queue)).Read<ReviewQueue>();
        var question = Assert.Single(due.Due);
        Assert.Equal(created.Refs["q"], question.Card.Id);
        Assert.Equal("Review queue", question.CanvasTitle);

        // Claude reads the queue but never grades it.
        Assert.Equal(HttpStatusCode.Forbidden, (await device.PostAsJsonAsync($"/api/v1/reviews/{question.Card.Id}", new ReviewRequest("good"), Api.Json)).StatusCode);
        var owner = await factory.Owner();
        var graded = await (await owner.PostAsJsonAsync($"/api/v1/reviews/{question.Card.Id}", new ReviewRequest("good"), Api.Json)).Read<ReviewResult>();
        Assert.Equal((1, 1.0), (graded.Review.Reps, graded.Review.IntervalDays));
        Assert.Equal(0, (await (await owner.GetAsync(queue)).Read<ReviewQueue>()).DueCount);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/reviews/{created.Refs["n"]}", new ReviewRequest("good"), Api.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/reviews/{question.Card.Id}", new ReviewRequest("meh"), Api.Json)).StatusCode);
    }

    [Fact]
    public async Task A_stale_version_is_refused_with_412_and_the_current_version()
    {
        var device = factory.Device();
        var canvas = await NewCanvas(device, "Stale test", Card("R", "a"));
        await (await device.Send(Post, $"/api/v1/canvases/{canvas.CanvasId}/batches", new BatchRequest([Card("R", "b")]), ifMatch: 1)).Read<BatchResult>();

        var stale = await device.Send(Post, $"/api/v1/canvases/{canvas.CanvasId}/batches", new BatchRequest([Card("R", "c")]), ifMatch: 1);
        var problem = await stale.Problem(HttpStatusCode.PreconditionFailed);
        Assert.Equal(ErrorCodes.StaleVersion, problem.GetProperty("code").GetString());
        Assert.Equal(2, problem.GetProperty("currentVersion").GetInt64());
        Assert.Equal("\"2\"", stale.Headers.ETag?.Tag);

        var fresh = await (await device.Send(Post, $"/api/v1/canvases/{canvas.CanvasId}/batches", new BatchRequest([Card("R", "c")]), ifMatch: 2)).Read<BatchResult>();
        Assert.Equal(3, fresh.Version);
    }

    [Fact]
    public async Task Repeating_a_request_id_returns_the_first_result_without_applying_twice()
    {
        var device = factory.Device();
        var canvas = await NewCanvas(device, "Retry test");
        var request = new BatchRequest([Card("R", "only once")], RequestId: "retry-1");

        var first = await (await device.Send(Post, $"/api/v1/canvases/{canvas.CanvasId}/batches", request)).Read<BatchResult>();
        var second = await (await device.Send(Post, $"/api/v1/canvases/{canvas.CanvasId}/batches", request)).Read<BatchResult>();

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Version, second.Version);
        var document = await (await device.GetAsync($"/api/v1/canvases/{canvas.CanvasId}")).Read<CanvasDocument>();
        Assert.Single(document.Cards);
        Assert.Equal(2, document.Version);

        var create = new CreateCanvasRequest("Idempotent create", RequestId: "create-retry-1");
        var a = await (await device.PostAsJsonAsync("/api/v1/canvases", create, Api.Json)).Read<BatchResult>();
        var b = await (await device.PostAsJsonAsync("/api/v1/canvases", create, Api.Json)).Read<BatchResult>();
        Assert.Equal(a.CanvasId, b.CanvasId);
    }

    [Fact]
    public async Task A_batch_with_one_bad_intent_changes_nothing()
    {
        var device = factory.Device();
        var canvas = await NewCanvas(device, "Atomic test", Card("R", "existing"));
        var response = await device.Send(Post, $"/api/v1/canvases/{canvas.CanvasId}/batches",
            new BatchRequest([Card("R", "would be added"), new UpdateCardIntent("cd_zzzzzzzz", Title: "x")]));
        var problem = await response.Problem(HttpStatusCode.NotFound);
        Assert.Contains("Intent 2", problem.GetProperty("detail").GetString());

        var document = await (await device.GetAsync($"/api/v1/canvases/{canvas.CanvasId}")).Read<CanvasDocument>();
        Assert.Equal(1, document.Version);
        Assert.Single(document.Cards);
    }

    [Fact]
    public async Task Malformed_input_is_a_400_problem_and_unknown_routes_are_json_404s()
    {
        var device = factory.Device();
        var canvas = await NewCanvas(device, "Malformed test");
        var bad = await device.PostAsync($"/api/v1/canvases/{canvas.CanvasId}/batches",
            new StringContent("""{"intents":[{"type":"explode"}]}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(ErrorCodes.InvalidRequest, (await bad.Problem(HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        var missing = await device.GetAsync("/api/v1/nothing-here");
        Assert.Equal(ErrorCodes.NotFound, (await missing.Problem(HttpStatusCode.NotFound)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Undo_and_revert_restore_state_and_search_follows()
    {
        var owner = await factory.Owner();
        var canvas = await NewCanvas(owner, "History test", Card("R", "Vector clocks", "v", body: "Lamport style ordering"));
        var url = $"/api/v1/canvases/{canvas.CanvasId}";
        var added = await (await owner.Send(Post, url + "/batches", new BatchRequest([Card("R", "Hybrid logical clocks", body: "zebrafish marker")]))).Read<BatchResult>();
        Assert.Single((await (await owner.GetAsync("/api/v1/search?q=zebrafish")).Read<SearchResult>()).Cards);

        var undone = await (await owner.Send(Post, $"{url}/batches/{added.Version}/undo")).Read<BatchResult>();
        Assert.StartsWith("Undid version 2", undone.Summary);
        Assert.Empty((await (await owner.GetAsync("/api/v1/search?q=zebrafish")).Read<SearchResult>()).Cards);

        var noPrecondition = await owner.Send(Post, url + "/revert", new RevertRequest(2));
        Assert.Equal(ErrorCodes.PreconditionRequired, (await noPrecondition.Problem((HttpStatusCode)428)).GetProperty("code").GetString());

        var reverted = await (await owner.Send(Post, url + "/revert", new RevertRequest(2), ifMatch: undone.Version)).Read<BatchResult>();
        Assert.Equal(4, reverted.Version);
        var hits = await (await owner.GetAsync("/api/v1/search?q=zebrafish")).Read<SearchResult>();
        Assert.Equal("Hybrid logical clocks", Assert.Single(hits.Cards).Title);

        var history = await (await owner.GetAsync(url + "/history")).Read<List<HistoryEntry>>();
        Assert.Equal([4L, 3, 2, 1], history.Select(entry => entry.Version));
        Assert.Equal(2, history[0].RevertsTo);
        Assert.Equal(2, history[1].Undoes);

        var past = await (await owner.GetAsync(url + "/state?version=1")).Read<CanvasDocument>();
        Assert.Equal(["Vector clocks"], past.Cards.Select(card => card.Title));
        var detail = await (await owner.GetAsync(url + "/history/2")).Read<BatchDetail>();
        Assert.Contains(detail.Ops, op => op.Type == "card.create" && op.Before is null && op.After is not null);
    }

    [Fact]
    public async Task Undo_refuses_when_later_edits_touched_the_same_cards()
    {
        var owner = await factory.Owner();
        var canvas = await NewCanvas(owner, "Undo conflict", Card("R", "first"));
        var url = $"/api/v1/canvases/{canvas.CanvasId}";
        var document = await (await owner.GetAsync(url)).Read<CanvasDocument>();
        await (await owner.Send(Post, url + "/batches", new BatchRequest([new UpdateCardIntent(document.Cards[0].Id, Title: "edited")]))).Read<BatchResult>();

        var response = await owner.Send(Post, url + "/batches/1/undo");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // version 1 (creation) is not undoable
        var conflict = await owner.Send(Post, url + "/batches/2/undo");
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var again = await owner.Send(Post, url + "/batches/2/undo");
        Assert.Equal(ErrorCodes.Conflict, (await again.Problem(HttpStatusCode.Conflict)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Snapshots_and_replay_reproduce_every_past_version()
    {
        var owner = await factory.Owner();
        var canvas = await NewCanvas(owner, "Snapshot test");
        var url = $"/api/v1/canvases/{canvas.CanvasId}";
        var expected = new Dictionary<long, List<string>>();
        for (var i = 0; i < 24; i++)
        {
            var result = await (await owner.Send(Post, url + "/batches", new BatchRequest([Card($"R{i % 3}", $"card {i}")]))).Read<BatchResult>();
            expected[result.Version] = [.. (await (await owner.GetAsync(url)).Read<CanvasDocument>()).Cards.Select(card => $"{card.Title}@{card.RegionId}:{card.X},{card.Y}").Order()];
        }
        foreach (var version in new long[] { 2, 9, 10, 11, 20, 25 })
        {
            var past = await (await owner.GetAsync($"{url}/state?version={version}")).Read<CanvasDocument>();
            Assert.Equal(expected[version], past.Cards.Select(card => $"{card.Title}@{card.RegionId}:{card.X},{card.Y}").Order());
        }
        var reverted = await (await owner.Send(Post, url + "/revert", new RevertRequest(12), ifMatch: 25)).Read<BatchResult>();
        var now = await (await owner.GetAsync(url)).Read<CanvasDocument>();
        Assert.Equal(expected[12], now.Cards.Select(card => $"{card.Title}@{card.RegionId}:{card.X},{card.Y}").Order());
        Assert.Equal(26, reverted.Version);
    }

    [Fact]
    public async Task Trash_hides_a_canvas_until_restored_and_purge_removes_it_for_good()
    {
        var owner = await factory.Owner();
        var device = factory.Device();
        var canvas = await NewCanvas(device, "Trash test", Card("R", "quokka marker"));
        var url = $"/api/v1/canvases/{canvas.CanvasId}";

        Assert.Equal(ErrorCodes.PreconditionRequired, (await (await device.DeleteAsync(url)).Problem((HttpStatusCode)428)).GetProperty("code").GetString());
        var trashed = await (await device.Send(HttpMethod.Delete, url, ifMatch: 1)).Read<BatchResult>();
        Assert.Equal(2, trashed.Version);
        await (await device.GetAsync(url)).Problem(HttpStatusCode.NotFound);
        Assert.Empty((await (await owner.GetAsync("/api/v1/search?q=quokka")).Read<SearchResult>()).Cards);
        Assert.Contains((await (await owner.GetAsync("/api/v1/canvases?trash=true")).Read<List<CanvasSummary>>()), c => c.Id == canvas.CanvasId);
        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync("/api/v1/canvases?trash=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await device.PostAsync(url + "/restore", null)).StatusCode);

        await (await owner.Send(Post, url + "/restore")).Read<BatchResult>();
        Assert.Single((await (await owner.GetAsync("/api/v1/search?q=quokka")).Read<SearchResult>()).Cards);

        Assert.Equal(HttpStatusCode.Forbidden, (await device.DeleteAsync(url + "/purge")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(url + "/purge")).StatusCode);
        await (await owner.GetAsync(url + "/history")).Problem(HttpStatusCode.NotFound);
        Assert.Empty((await (await owner.GetAsync("/api/v1/search?q=quokka")).Read<SearchResult>()).Cards);
    }

    [Fact]
    public async Task The_device_principal_cannot_set_positions()
    {
        var device = factory.Device();
        var canvas = await NewCanvas(device, "Coordinates test", Card("R", "a"));
        var document = await (await device.GetAsync($"/api/v1/canvases/{canvas.CanvasId}")).Read<CanvasDocument>();
        var response = await device.Send(Post, $"/api/v1/canvases/{canvas.CanvasId}/batches",
            new BatchRequest([new SetLayoutIntent([new LayoutItem(document.Cards[0].Id, 500, 500)])]));
        Assert.Equal(ErrorCodes.ForbiddenForActor, (await response.Problem(HttpStatusCode.Forbidden)).GetProperty("code").GetString());

        var owner = await factory.Owner();
        var moved = await (await owner.Send(Post, $"/api/v1/canvases/{canvas.CanvasId}/batches",
            new BatchRequest([new SetLayoutIntent([new LayoutItem(document.Cards[0].Id, 500, 500)])]))).Read<BatchResult>();
        Assert.True(moved.Changed);
    }

    [Fact]
    public async Task Outline_cards_query_list_filters_and_patch_work_together()
    {
        var device = factory.Device();
        var canvas = await NewCanvas(device, "Outline test", Card("Ideas", "Bloom filters", "b", body: "Probabilistic set membership."), Card("Ideas", "Cuckoo filters", "c"),
            new ConnectIntent("b", "c", ConnectionKinds.Relates, "alternatives"));
        var url = $"/api/v1/canvases/{canvas.CanvasId}";

        var outline = await device.GetAsync(url + "/outline?depth=short");
        Assert.Equal("text/plain", outline.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"1\"", outline.Headers.ETag?.Tag);
        var text = await outline.Content.ReadAsStringAsync();
        Assert.Contains("## Ideas", text);
        Assert.Contains("Bloom filters — Probabilistic set membership.", text);

        var patched = await (await device.Send(HttpMethod.Patch, url, new UpdateCanvasIntent(Tags: ["Data Structures"], Collection: "CS"), ifMatch: 1)).Read<BatchResult>();
        Assert.Equal(2, patched.Version);
        var listed = await (await device.GetAsync("/api/v1/canvases?tag=data-structures")).Read<List<CanvasSummary>>();
        var summary = Assert.Single(listed, c => c.Id == canvas.CanvasId);
        Assert.Equal((2, 1, 0), (summary.CardCount, summary.RegionCount, summary.OpenQuestions));

        var bloom = (await (await device.GetAsync(url)).Read<CanvasDocument>()).Cards.Single(card => card.Title == "Bloom filters");
        var details = await (await device.PostAsJsonAsync("/api/v1/cards/query", new CardsQuery([bloom.Id, "cd_zzzzzzzz"]), Api.Json)).Read<List<CardDetail>>();
        var detail = Assert.Single(details);
        Assert.Equal(("Ideas", canvas.CanvasId), (detail.RegionTitle, detail.CanvasId));
        Assert.Equal("alternatives", Assert.Single(detail.Connections).Label);
    }

    [Fact]
    public async Task Live_events_stream_committed_batches_and_replay_missed_ones()
    {
        var owner = await factory.Owner();
        var device = factory.Device();
        var canvas = await NewCanvas(device, "Live test", Card("R", "before"));
        var url = $"/api/v1/canvases/{canvas.CanvasId}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url + "/events");
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "0");
        using var response = await owner.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        var replayed = await NextEvent(reader);
        Assert.Equal(1, replayed.Version);
        await (await device.Send(Post, url + "/batches", new BatchRequest([Card("R", "after")]))).Read<BatchResult>();
        var live = await NextEvent(reader);
        Assert.Equal(2, live.Version);
        Assert.Equal(ActorKinds.Mcp, live.Actor);
        var created = Assert.Single(live.Ops, op => op.Type == "card.create");
        Assert.Equal("after", created.After!.Value.GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync(url + "/events")).StatusCode);
    }

    private static async Task<CanvasEvent> NextEvent(StreamReader reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var line = await reader.ReadLineAsync(timeout.Token) ?? throw new EndOfStreamException();
            if (line.StartsWith("data: ", StringComparison.Ordinal) && line.Contains("\"ops\"", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<CanvasEvent>(line[6..], Api.Json)!;
        }
    }
}
