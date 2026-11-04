using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class OutlineTests
{
    [Fact]
    public void The_outline_lists_regions_cards_and_connections_without_coordinates()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new UpdateCanvasIntent(Description: "How Raft works.", Tags: ["distributed-systems"]));
        canvas.Run(Mcp,
            new AddCardIntent(CardKinds.Idea, "Randomized timeouts", "Leader election", "t", "Each follower waits 150–300 ms.\nSo split votes are rare."),
            new AddCardIntent(CardKinds.Question, "What about even clusters?", "Leader election", "q"),
            new AddRegionIntent("Terms", Parent: "Leader election"),
            new AddCardIntent(CardKinds.Link, "Raft paper", "Terms", "p", Url: "https://raft.github.io/raft.pdf"),
            new ConnectIntent("t", "q", ConnectionKinds.LeadsTo, "split votes"));

        var outline = OutlineRenderer.Render(canvas.State, 3, new OutlineOptions());

        var election = canvas.Region("Leader election");
        var timeouts = canvas.Card("Randomized timeouts");
        Assert.StartsWith($"# Raft consensus  cv_00000001  v3  tags: distributed-systems\n> How Raft works.\n2 regions · 3 cards · 1 open questions\n", outline);
        Assert.Contains($"\n## Leader election  {election.Id}  (2 cards)\n", outline);
        Assert.Contains($"- {timeouts.Id} idea Randomized timeouts — Each follower waits 150–300 ms. So split votes are rare.\n", outline);
        Assert.Contains("? What about even clusters?", outline);
        Assert.Contains($"\n### Terms  {canvas.Region("Terms").Id}  (1 card)\n", outline);
        Assert.Contains("link Raft paper <https://raft.github.io/raft.pdf>", outline);
        Assert.Contains($"{timeouts.Id} -leads-to-> {canvas.Card("What about even clusters?").Id} \"split votes\"", outline);
        Assert.DoesNotContain("\"x\"", outline);
        Assert.DoesNotMatch(@"\b(x|y|w|h)\s*[:=]\s*\d", outline);
    }

    [Fact]
    public void Depths_control_how_much_body_text_is_included()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddCardIntent(CardKinds.Note, "Long", "R", Body: "line one\n" + new string('b', 300)));

        Assert.DoesNotContain("Long", OutlineRenderer.Render(canvas.State, 2, new OutlineOptions(OutlineDepths.Regions)));
        Assert.DoesNotContain("line one", OutlineRenderer.Render(canvas.State, 2, new OutlineOptions(OutlineDepths.Titles)));
        var shortOutline = OutlineRenderer.Render(canvas.State, 2, new OutlineOptions(OutlineDepths.Short));
        Assert.Contains("— line one bbbb", shortOutline);
        Assert.Contains("…\n", shortOutline);
        Assert.Contains("  " + new string('b', 300) + "\n", OutlineRenderer.Render(canvas.State, 2, new OutlineOptions(OutlineDepths.Full)));
    }

    [Fact]
    public void Large_canvases_are_truncated_with_a_pointer_to_region_reads()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Enumerable.Range(0, 30).Select(i => (Intent)Note(i < 20 ? "Big" : "Small", $"c{i}")).ToArray());

        var outline = OutlineRenderer.Render(canvas.State, 2, new OutlineOptions(MaxCards: 10));
        Assert.Contains("…truncated: 20 more cards in 2 regions; call get_canvas with region=<id>", outline);

        var focused = OutlineRenderer.Render(canvas.State, 2, new OutlineOptions(Region: "small"));
        Assert.Contains("## Small", focused);
        Assert.DoesNotContain("## Big", focused);
    }
}
