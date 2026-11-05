using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class ExchangeTests
{
    private static Sandbox Raft()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new UpdateCanvasIntent(Description: "How Raft works.", Tags: ["distributed-systems"]));
        canvas.Run(Mcp,
            new AddCardIntent(CardKinds.Idea, "Randomized timeouts", "Leader election", "t", "Followers wait 150–300 ms.\nSee [[Terms]].", Emphasis: Emphases.Key, Tint: "blue"),
            new AddCardIntent(CardKinds.Question, "What about even clusters?", "Leader election", "q"),
            new AddRegionIntent("Terms region", Parent: "Leader election"),
            new AddCardIntent(CardKinds.Link, "Raft paper", "Terms region", "p", "The paper.", Url: "https://raft.github.io/raft.pdf"),
            new AddCardIntent(CardKinds.Code, "Up-to-date", "Terms region", "c", "return a > b", Language: "go"),
            new ConnectIntent("t", "q", ConnectionKinds.LeadsTo, "split votes"));
        return canvas;
    }

    [Fact]
    public void Markdown_reads_as_a_document_with_regions_as_headings()
    {
        var md = Exchange.Markdown(Raft().State);

        Assert.StartsWith("# Raft consensus\n\n> How Raft works.\n\nTags: distributed-systems\n\n## Leader election\n\n", md);
        Assert.Contains("- **Randomized timeouts** ★\n  Followers wait 150–300 ms.\n  See [[Terms]].\n", md);
        Assert.Contains("- **Q:** **What about even clusters?**\n", md);
        Assert.Contains("### Terms region\n\n- **[Raft paper](https://raft.github.io/raft.pdf)**\n  The paper.\n", md);
        Assert.Contains("  ```go\n  return a > b\n  ```\n", md);
        Assert.EndsWith("## Connections\n\n- Randomized timeouts —leads-to→ What about even clusters? (split votes)\n", md);
    }

    [Fact]
    public void Json_canvas_round_trips_regions_cards_kinds_tints_and_connections()
    {
        var source = Raft();
        var exported = Exchange.ToJsonCanvas(source.State);

        Assert.Equal(["group", "group"], exported.Nodes!.Take(2).Select(n => n.Type));
        var sub = exported.Nodes!.Single(n => n.Label == "Terms region");
        var parent = exported.Nodes!.Single(n => n.Label == "Leader election");
        Assert.True(sub.X > parent.X && sub.Y > parent.Y, "sub-regions are exported at absolute positions");
        Assert.Equal("5", exported.Nodes!.Single(n => n.Text?.StartsWith("## Randomized", StringComparison.Ordinal) == true).Color);
        var edge = Assert.Single(exported.Edges!);
        Assert.Equal(("arrow", "split votes"), (edge.ToEnd, edge.Label));

        var copy = new Sandbox("Copy");
        var result = copy.Run(new BatchRequest(Exchange.FromJsonCanvas(exported)), Actor.Owner);

        Assert.Empty(result.Skipped);
        Assert.Equal(source.State.Regions.Values.Select(r => (r.Title, Parent: r.ParentId is null)).OrderBy(x => x.Title),
            copy.State.Regions.Values.Select(r => (r.Title, Parent: r.ParentId is null)).OrderBy(x => x.Title));
        Assert.Equal(source.State.Cards.Values.Select(c => (c.Title, c.Kind, c.Body, c.Url, c.Language, c.Tint, Region: source.State.Regions[c.RegionId!].Title)).OrderBy(x => x.Title),
            copy.State.Cards.Values.Select(c => (c.Title, c.Kind, c.Body, c.Url, c.Language, c.Tint, Region: copy.State.Regions[c.RegionId!].Title)).OrderBy(x => x.Title));
        var connection = Assert.Single(copy.State.Connections.Values);
        Assert.Equal(("relates", "split votes"), (connection.Kind, connection.Label));
    }

    [Fact]
    public void Import_handles_obsidian_canvases_loose_nodes_deep_groups_and_kind_labels()
    {
        var canvas = new JsonCanvasDocument(
            [
                new("outer", "group", 0, 0, 1000, 1000, Label: "Outer"),
                new("inner", "group", 50, 50, 600, 600, Label: "Inner"),
                new("deepest", "group", 100, 100, 300, 300, Label: "Deepest"),
                new("a", "text", 120, 120, 100, 50, Text: "**Bold title**\nbody"),
                new("b", "text", 2000, 2000, 100, 50, Color: "1", Text: "Why does it fail?"),
                new("c", "link", 700, 700, 100, 50, Url: "https://example.com/post/"),
                new("d", "file", 720, 720, 100, 50, File: "notes/Plan.md"),
                new("e", "text", 740, 740, 100, 50, Text: "   "),
            ],
            [
                new("e1", "a", "b", Label: "depends-on"),
                new("e2", "b", "c", Color: "1"),
                new("e3", "a", "missing"),
            ]);
        var warnings = new List<string>();
        var sandbox = new Sandbox("Imported");
        sandbox.Run(new BatchRequest(Exchange.FromJsonCanvas(canvas, warnings)), Actor.Owner);
        var state = sandbox.State;

        var deepest = sandbox.Region("Deepest");
        Assert.Equal(sandbox.Region("Outer").Id, deepest.ParentId); // flattened to one level of nesting
        Assert.Equal(deepest.Id, sandbox.Card("Bold title").RegionId);
        Assert.Equal("body", sandbox.Card("Bold title").Body);
        var question = sandbox.Card("Why does it fail?");
        Assert.Equal((CardKinds.Question, "red", Exchange.UnsortedRegion), (question.Kind, question.Tint, state.Regions[question.RegionId!].Title));
        Assert.Equal((CardKinds.Link, "https://example.com/post/"), (sandbox.Card("example.com/post").Kind, sandbox.Card("example.com/post").Url));
        Assert.Equal("File: `notes/Plan.md`", sandbox.Card("Plan").Body);
        Assert.Equal([ConnectionKinds.DependsOn, ConnectionKinds.Contradicts], state.Connections.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).Select(c => c.Kind));
        Assert.Equal(2, warnings.Count);
    }
}
