using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

/// <summary>Causal-loop diagrams and scenario marks.</summary>
public sealed class SystemsTests
{
    // Two loops through "Users": more users → more content → more users (R); more users → more load → slower → fewer users (B, with a delay).
    private const string Growth = "flowchart LR\n  users[Users] -->|+| content[Content]\n  content -->|+| users\n  users -->|+| load[Server load]\n" +
                                  "  load -->|+| latency[Latency]\n  latency -->|- delay| users";

    [Fact]
    public void Loops_are_found_signed_and_named_and_the_outline_lists_them()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("Growth", Growth, "g", Layout: RegionLayouts.Loops));
        var region = canvas.Region("Growth");
        Assert.Equal(RegionLayouts.Loops, region.Layout);

        var loops = CausalLoops.Find(canvas.State, region.Id);
        Assert.Equal(2, loops.Count);
        var balancing = loops[0];
        Assert.Equal("B1", balancing.Name);
        Assert.Equal('B', balancing.Polarity);
        Assert.True(balancing.Delayed);
        Assert.Equal([result.Refs["g:users"], result.Refs["g:load"], result.Refs["g:latency"]], balancing.Members);
        var reinforcing = loops[1];
        Assert.Equal("R1", reinforcing.Name);
        Assert.False(reinforcing.Delayed);
        Assert.Equal(2, reinforcing.Connections.Count);

        var outline = OutlineRenderer.Render(canvas.State, 1, new OutlineOptions(OutlineDepths.Titles));
        Assert.Contains("causal-loop diagram", outline);
        Assert.Contains("~ loop B1 (balancing, with a delay): Users → Server load → Latency → Users", outline);
        Assert.Contains("~ loop R1 (reinforcing): Users → Content → Users", outline);
    }

    [Theory]
    [InlineData("+", 1)]
    [InlineData("- ||", -1)]
    [InlineData("−", -1)]
    [InlineData("o", -1)]
    [InlineData("s", 1)]
    [InlineData("causes", 0)]
    [InlineData(null, 0)]
    public void Polarity_comes_from_the_label(string? label, int sign) => Assert.Equal(sign, CausalLoops.Sign(label));

    [Fact]
    public void An_unsigned_link_leaves_a_loop_without_polarity()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddDiagramIntent("Loop", "flowchart LR\n  a[A] -->|+| b[B]\n  b --> a", "l", Layout: RegionLayouts.Loops));
        var loop = Assert.Single(CausalLoops.Find(canvas.State, canvas.Region("Loop").Id));
        Assert.Null(loop.Polarity);
        Assert.Equal("L1", loop.Name);
    }

    [Fact]
    public void A_loops_diagram_puts_its_variables_on_a_circle_and_draws_badges()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddDiagramIntent("Growth", Growth, "g", Layout: RegionLayouts.Loops));
        var region = canvas.Region("Growth");
        var cards = canvas.State.CardsIn(region.Id).ToList();
        var centres = cards.Select(c => (X: c.X + c.W / 2.0, Y: c.Y + c.H / 2.0)).ToList();
        var (cx, cy) = (centres.Average(c => c.X), centres.Average(c => c.Y));
        var radii = centres.Select(c => Math.Sqrt((c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy))).ToList();
        Assert.True(radii.Max() - radii.Min() < radii.Average() * 0.35, string.Join(", ", radii));
        Assert.All(cards, c => Assert.True(c.X >= 0 && c.Y >= 0 && c.X + c.W <= region.W && c.Y + c.H <= region.H));
        // No two shapes overlap.
        foreach (var a in cards)
            foreach (var b in cards.Where(b => string.CompareOrdinal(b.Id, a.Id) > 0))
                Assert.False(a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H, $"{a.Title} overlaps {b.Title}");

        var svg = SvgRenderer.Render(canvas.State, region);
        Assert.Contains("data-loop=\"R1\"", svg);
        Assert.Contains("data-loop=\"B1\"", svg);
        Assert.Contains(" Q", svg);
    }

    [Fact]
    public void Steps_mark_items_for_a_scenario()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp,
            new AddDiagramIntent("Orders", "flowchart LR\n  api[API] --> broker[(Broker)]\n  broker --> worker[Worker]", "d"),
            new AddWalkthroughIntent("d", "Broker outage",
            [
                new WalkthroughStep("Normal.", ["d:api", "d:worker"]),
                new WalkthroughStep("The broker goes down; the API keeps accepting orders.", ["d:api"], [new StepMark("d:broker", "Down", "503s"), new StepMark("API", "hot")]),
            ]));
        var steps = Assert.Single(canvas.State.Walkthroughs.Values).Steps;
        Assert.Null(steps[0].Marks);
        Assert.Equal([new StepMark(result.Refs["d:broker"], MarkStates.Down, "503s"), new StepMark(result.Refs["d:api"], MarkStates.Hot)], steps[1].Marks);

        var outline = OutlineRenderer.Render(canvas.State, 1, new OutlineOptions(OutlineDepths.Full));
        Assert.Contains($"{{{result.Refs["d:broker"]} down \"503s\", {result.Refs["d:api"]} hot}}", outline);

        Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddWalkthroughIntent("Orders", "Bad", [new("x", null, [new StepMark("API", "exploded")])])));
        Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddWalkthroughIntent("Orders", "Bad", [new("x", null, [new StepMark("Kafka", "down")])])));
        Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddWalkthroughIntent("Orders", "Bad", [new("x", null, [new StepMark("API", "down"), new StepMark("d:api", "hot")])])));
    }
}
