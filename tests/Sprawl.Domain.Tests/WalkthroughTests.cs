using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class WalkthroughTests
{
    private const string Pay = "flowchart LR\n  web[Web app] --> api{{API}}\n  api ==> orders[Orders]\n  orders --> db[(Orders DB)]";

    [Fact]
    public void A_walkthrough_resolves_refs_titles_and_ids_when_it_is_written()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp,
            new AddDiagramIntent("Payments", Pay, "d"),
            new AddWalkthroughIntent("d", "How a payment flows",
            [
                new WalkthroughStep("The browser calls the API.", ["d:web", "d:api"]),
                new WalkthroughStep("The API hands the order to the **Orders** service.", ["Orders"]),
                new WalkthroughStep("Orders writes it down.", ["d:orders", "Orders DB"]),
                new WalkthroughStep("That is the whole path."),
            ], "w"));

        var walkthrough = Assert.Single(canvas.State.Walkthroughs.Values);
        Assert.Equal(result.Refs["w"], walkthrough.Id);
        Assert.StartsWith("wt_", walkthrough.Id);
        Assert.Equal(canvas.Region("Payments").Id, walkthrough.RegionId);
        Assert.Equal([result.Refs["d:web"], result.Refs["d:api"]], walkthrough.Steps[0].Highlight);
        Assert.Equal([result.Refs["d:orders"]], walkthrough.Steps[1].Highlight);
        Assert.Equal([result.Refs["d:orders"], result.Refs["d:db"]], walkthrough.Steps[2].Highlight);
        Assert.Empty(walkthrough.Steps[3].Highlight!);
        Assert.Contains("+1 walkthrough", result.Summary);

        var outline = OutlineRenderer.Render(canvas.State, 2, new OutlineOptions(OutlineDepths.Full));
        Assert.Contains($"~ walkthrough {walkthrough.Id} How a payment flows (4 steps)", outline);
        Assert.Contains("  2. The API hands the order to the **Orders** service. [" + result.Refs["d:orders"] + "]", outline);
    }

    [Fact]
    public void Unknown_names_fail_with_the_step_and_the_name_and_duplicates_need_replace()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddDiagramIntent("Payments", Pay, "d"));
        var missing = Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddWalkthroughIntent("Payments", "Tour", [new("ok", ["API"]), new("bad", ["Kafka"])])));
        Assert.Contains("Step 2", missing.Message);
        Assert.Contains("Kafka", missing.Message);

        canvas.Run(Mcp, new AddWalkthroughIntent("Payments", "Tour", [new("one", ["API"])]));
        Assert.Equal(ErrorCodes.Duplicate, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddWalkthroughIntent("Payments", "tour", [new("two")]))).Code);
        canvas.Run(Mcp, new AddWalkthroughIntent("Payments", "tour", [new("two"), new("three")], Replace: true));
        Assert.Equal(["two", "three"], Assert.Single(canvas.State.Walkthroughs.Values).Steps.Select(s => s.Text));
        Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddWalkthroughIntent("Payments", "Empty", [])));
    }

    [Fact]
    public void Walkthroughs_undo_and_go_with_their_region()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddDiagramIntent("Payments", Pay, "d"));
        var before = canvas.State.Clone();
        var added = canvas.Run(Mcp, new AddWalkthroughIntent("Payments", "Tour", [new("Look", ["API"])]));

        foreach (var op in Enumerable.Reverse(added.Ops)) StateOps.Apply(canvas.State, op.Inverse());
        Assert.Empty(StateOps.Diff(before, canvas.State));

        canvas.Run(Mcp, new AddWalkthroughIntent("Payments", "Tour", [new("Look", ["API"])]));
        var region = canvas.Region("Payments");
        var cards = canvas.State.CardsIn(region.Id).Select(c => c.Id).ToList();
        canvas.Run(Mcp, new DeleteCardsIntent(cards), new DeleteRegionIntent(region.Id));
        Assert.Empty(canvas.State.Walkthroughs);
        Assert.Empty(StateOps.IntegrityProblems(canvas.State));
    }

    [Fact]
    public void Equal_walkthroughs_compare_equal_so_reloaded_state_does_not_diff()
    {
        var a = new Walkthrough("wt_00000001", "rg_00000001", "T", [new("x", ["cd_00000001"])], "ai", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var b = a with { Steps = [new("x", ["cd_00000001"])] };
        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { Steps = [new("y", ["cd_00000001"])] });
    }
}
