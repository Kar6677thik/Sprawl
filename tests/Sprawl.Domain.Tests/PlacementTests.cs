using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class PlacementTests
{
    [Fact]
    public void Cards_flow_into_the_shortest_column_of_a_new_region()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("Leader election", "a"), Note("Leader election", "b", 300), Note("Leader election", "c"));

        var region = canvas.Region("Leader election");
        Assert.Equal(2, region.Cols); // three cards → two columns
        Assert.Equal((0, 0), (region.X, region.Y));
        Assert.Equal((24, 48), (canvas.Card("a").X, canvas.Card("a").Y));
        Assert.Equal((320, 48), (canvas.Card("b").X, canvas.Card("b").Y));
        Assert.Equal((24, 48 + 88 + 16), (canvas.Card("c").X, canvas.Card("c").Y)); // column 0 is shorter than b's
        Assert.Equal(Geometry.RegularHeight, canvas.Card("b").H);
        Assert.Equal(Geometry.MinRegionWidth(2), region.W);
        Assert.Equal(48 + 88 + 16 + 88 + 24, region.H); // column 0 (a, c) ends lower than column 1 (b)
        Assert.All(canvas.State.Cards.Values, card => Assert.Equal(Authors.Ai, card.CreatedBy));
    }

    [Fact]
    public void New_regions_pack_in_rows_on_the_frontier()
    {
        var canvas = new Sandbox();
        foreach (var title in new[] { "One", "Two", "Three", "Four" })
            canvas.Run(Mcp, new AddRegionIntent(title));

        var width = Geometry.MinRegionWidth(2);
        Assert.Equal((0, 0), (canvas.Region("One").X, canvas.Region("One").Y));
        Assert.Equal((width + 64, 0), (canvas.Region("Two").X, canvas.Region("Two").Y));
        Assert.Equal((2 * (width + 64), 0), (canvas.Region("Three").X, canvas.Region("Three").Y));
        // 3 × 632 + 2 × 64 = 2024 fits the 2400 frontier; a fourth would not, so it starts the next row.
        Assert.Equal((0, Geometry.MinRegionHeight + 64), (canvas.Region("Four").X, canvas.Region("Four").Y));
    }

    [Fact]
    public void A_region_connected_to_an_existing_one_is_placed_beside_it()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddRegionIntent("Far"), new AddRegionIntent("Elections"));
        canvas.Run(Mcp, new AddRegionIntent("Row filler"));
        canvas.Run(Mcp, Note("Elections", "Timeouts", reference: "t"));
        var timeouts = canvas.Card("Timeouts").Id;

        canvas.Run(Mcp, new AddRegionIntent("Terms", Ref: "terms"), Note("terms", "Term numbers", reference: "n"),
            new ConnectIntent("n", timeouts, ConnectionKinds.LeadsTo));

        var elections = canvas.Region("Elections");
        var terms = canvas.Region("Terms");
        // Right of Elections is taken by "Row filler", so the nearest free candidate is directly below it.
        Assert.Equal(elections.X, terms.X);
        Assert.Equal(elections.Y + elections.H + 64, terms.Y);
    }

    [Fact]
    public void A_related_region_goes_right_of_a_tall_anchor_rather_than_above_it()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Enumerable.Range(0, 8).Select(i => (Intent)Note("Tall", $"t{i}", 700)).ToArray());
        var anchorCard = canvas.Card("t0").Id;

        canvas.Run(Mcp, Note("Related", "r", reference: "r"), new ConnectIntent("r", anchorCard));

        var tall = canvas.Region("Tall");
        var related = canvas.Region("Related");
        Assert.Equal((tall.X + tall.W + 64, tall.Y), (related.X, related.Y));
        Assert.All(canvas.State.Regions.Values, region => Assert.True(region.Y >= 0));
    }

    [Fact]
    public void A_growing_region_pushes_auto_regions_below_it_down()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddRegionIntent("A"), new AddRegionIntent("B"), new AddRegionIntent("C"), new AddRegionIntent("Below"));
        var below = canvas.Region("Below");
        Assert.Equal(0, below.X);

        canvas.Run(Mcp, Enumerable.Range(0, 6).Select(i => (Intent)Note("A", $"card {i}", 700)).ToArray());

        var a = canvas.Region("A");
        var moved = canvas.Region("Below");
        Assert.Equal(a.Y + a.H + 64, moved.Y);
        Assert.Equal(below.X, moved.X);
    }

    [Fact]
    public void A_longer_body_grows_the_card_and_pushes_the_cards_below_it()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("R", "top"), Note("R", "right"), Note("R", "under"));
        var under = canvas.Card("under");
        Assert.Equal(canvas.Card("top").X, under.X);

        canvas.Run(Mcp, new UpdateCardIntent(canvas.Card("top").Id, Body: new string('y', 900)));

        var top = canvas.Card("top");
        Assert.Equal(Geometry.TallHeight, top.H);
        Assert.Equal(top.Y + top.H + 16, canvas.Card("under").Y);
    }

    [Fact]
    public void A_tall_region_widens_instead_of_growing_down_forever()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("R", "seed"));
        Assert.Equal(2, canvas.Region("R").Cols);
        for (var i = 0; i < 12; i++) canvas.Run(Mcp, Note("R", $"tall {i}", 900));
        var region = canvas.Region("R");
        Assert.True(region.Cols > 2);
        Assert.True(region.H - Geometry.Header <= Geometry.TallnessLimit * Geometry.ContentWidth(region.Cols) + Geometry.TallHeight + Geometry.Gap);
    }

    [Fact]
    public void Adding_cards_never_moves_existing_cards()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Enumerable.Range(0, 7).Select(i => (Intent)Note(i % 2 == 0 ? "Even" : "Odd", $"c{i}", i * 97)).ToArray());
        var before = canvas.State.Cards.Values.ToDictionary(card => card.Id, card => (card.RegionId, card.X, card.Y));

        canvas.Run(Mcp, Enumerable.Range(7, 9).Select(i => (Intent)Note(i % 2 == 0 ? "Even" : "Odd", $"c{i}", i * 61)).ToArray());

        foreach (var (id, position) in before)
            Assert.Equal(position, (canvas.State.Cards[id].RegionId, canvas.State.Cards[id].X, canvas.State.Cards[id].Y));
    }

    [Fact]
    public void An_auto_card_skips_past_a_card_placed_by_hand()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("R", "a"), Note("R", "b"));
        var region = canvas.Region("R");
        canvas.Run(Actor.Owner, new SetLayoutIntent([new LayoutItem(canvas.Card("b").Id, 24, 152), new LayoutItem(canvas.Card("a").Id, 320, 48)]));

        canvas.Run(Mcp, Note("R", "new"));

        var placed = canvas.Card("new");
        var pinned = canvas.Card("b");
        Assert.False(placed.X == pinned.X && placed.Y < pinned.Y + pinned.H + 16 && pinned.Y < placed.Y + placed.H + 16);
        Assert.Equal(region.Id, placed.RegionId);
    }

    [Fact]
    public void Sub_regions_are_packed_inside_their_parent_below_its_cards()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("Raft", "one"), Note("Raft", "two"),
            new AddRegionIntent("Log replication", Ref: "log", Parent: "Raft"), Note("log", "Append entries"));

        var parent = canvas.Region("Raft");
        var sub = canvas.Region("Log replication");
        Assert.Equal(parent.Id, sub.ParentId);
        Assert.Equal(Geometry.Padding, sub.X);
        Assert.Equal(Geometry.Header + Geometry.CompactHeight + Geometry.Gap, sub.Y);
        Assert.True(parent.H >= sub.Y + sub.H + Geometry.Padding);
        Assert.True(parent.W >= sub.X + sub.W + Geometry.Padding);
    }

    [Fact]
    public void Regions_nest_one_level_only()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddRegionIntent("Top"), new AddRegionIntent("Middle", Parent: "Top"));
        var error = Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddRegionIntent("Bottom", Parent: "Middle")));
        Assert.Equal(ErrorCodes.InvalidRequest, error.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    public void Random_sessions_keep_every_placement_invariant(int seed)
    {
        var random = new Random(seed);
        var canvas = new Sandbox();
        string[] regions = ["Consensus", "Elections", "Log", "Safety", "Membership", "Snapshots"];
        for (var step = 0; step < 60; step++)
        {
            var before = canvas.State.Clone();
            if (random.Next(5) == 0 && canvas.State.Cards.Count > 0)
            {
                // The owner drags something: it becomes manual.
                var cards = canvas.State.Cards.Values.OrderBy(card => card.Id).ToList();
                var card = cards[random.Next(cards.Count)];
                canvas.Run(Actor.Owner, new SetLayoutIntent([new LayoutItem(card.Id, random.Next(24, 900), random.Next(48, 1200))]));
                continue;
            }
            var intents = new List<Intent>();
            for (var i = random.Next(1, 8); i > 0; i--)
                intents.Add(Note(regions[random.Next(regions.Length)], $"card {step}-{i}", random.Next(0, 1200)));
            if (random.Next(3) == 0 && canvas.State.Cards.Count > 0)
            {
                var victim = canvas.State.Cards.Values.OrderBy(card => card.Id).ElementAt(random.Next(canvas.State.Cards.Count));
                intents.Add(new UpdateCardIntent(victim.Id, Body: new string('z', random.Next(0, 1500))));
            }
            canvas.Run(Mcp, intents.ToArray());

            AssertManualItemsUnchanged(before, canvas.State);
            AssertNoOverlapsInvolvingAutoItems(canvas.State);
            Assert.Empty(StateOps.IntegrityProblems(canvas.State));
        }
    }

    [Fact]
    public void The_same_history_produces_the_same_layout()
    {
        static CanvasState Play()
        {
            var canvas = new Sandbox();
            var random = new Random(99);
            for (var step = 0; step < 25; step++)
                canvas.Run(Mcp, Enumerable.Range(0, random.Next(1, 6)).Select(i => (Intent)Note($"R{random.Next(4)}", $"s{step}i{i}", random.Next(900))).ToArray());
            return canvas.State;
        }
        Assert.Empty(StateOps.Diff(Play(), Play()));
    }

    private static void AssertManualItemsUnchanged(CanvasState before, CanvasState after)
    {
        foreach (var card in before.Cards.Values.Where(card => card.Placement == Placements.Manual))
        {
            var now = after.Cards[card.Id];
            Assert.Equal((card.RegionId, card.X, card.Y, card.W), (now.RegionId, now.X, now.Y, now.W));
        }
        foreach (var region in before.Regions.Values.Where(region => region.Placement == Placements.Manual))
            Assert.Equal((region.X, region.Y), (after.Regions[region.Id].X, after.Regions[region.Id].Y));
    }

    private static void AssertNoOverlapsInvolvingAutoItems(CanvasState state)
    {
        bool Manual(string id) => EntityIds.KindOf(id) == EntityKind.Card
            ? state.Cards[id].Placement == Placements.Manual
            : state.Regions[id].Placement == Placements.Manual;
        var offending = LayoutEngine.FindOverlaps(state).Where(w => !(Manual(w.FirstId) && Manual(w.SecondId))).ToList();
        Assert.Empty(offending);
    }
}
