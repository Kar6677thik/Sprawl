using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class BatchTests
{
    [Fact]
    public void Temp_refs_connect_cards_created_in_the_same_batch()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp,
            new AddRegionIntent("Elections", Ref: "el"),
            new AddCardIntent(CardKinds.Idea, "Randomized timeouts", "el", "a"),
            new AddCardIntent(CardKinds.Question, "Split votes?", "el", "b"),
            new ConnectIntent("a", "b", ConnectionKinds.LeadsTo, "causes"));

        var connection = Assert.Single(canvas.State.Connections.Values);
        Assert.Equal(result.Refs["a"], connection.SourceId);
        Assert.Equal(result.Refs["b"], connection.TargetId);
        Assert.Equal("causes", connection.Label);
        Assert.Equal(canvas.Region("Elections").Id, result.Refs["el"]);
        Assert.Equal("+2 cards in Elections, +1 region, +1 connection", result.Summary);
    }

    [Fact]
    public void Duplicate_titles_are_skipped_and_their_ref_points_at_the_existing_card()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("R", "Quorum"));
        var existing = canvas.Card("Quorum").Id;

        var result = canvas.Run(Mcp, Note("R", "quorum.", reference: "q"), Note("R", "Majority", reference: "m"), new ConnectIntent("m", "q"));

        Assert.Single(canvas.State.Cards.Values, card => Text.SameTitle(card.Title, "Quorum"));
        Assert.Equal(existing, result.Refs["q"]);
        Assert.Contains(result.Skipped, item => item is { Reason: "duplicate", Ref: "q" } && item.ExistingId == existing);
        Assert.Equal(existing, Assert.Single(canvas.State.Connections.Values).TargetId);

        var failure = Assert.Throws<SprawlException>(() => canvas.Run(new BatchRequest([Note("R", "Quorum")], OnDuplicate: DuplicatePolicies.Fail), Mcp));
        Assert.Equal(ErrorCodes.Duplicate, failure.Code);
    }

    [Fact]
    public void A_failing_intent_rejects_the_whole_batch()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("R", "keep"));
        var before = canvas.State.Clone();

        Assert.Throws<SprawlException>(() => canvas.Run(Mcp, Note("R", "new one"), new UpdateCardIntent("cd_missing00", Title: "x")));

        Assert.Empty(StateOps.Diff(before, canvas.State));
    }

    [Fact]
    public void The_ai_cannot_place_or_move_by_coordinates()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("R", "a"));
        var id = canvas.Card("a").Id;
        Assert.Equal(ErrorCodes.ForbiddenForActor, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new SetLayoutIntent([new LayoutItem(id, 0, 0)]))).Code);
        Assert.Equal(ErrorCodes.ForbiddenForActor, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddCardIntent(CardKinds.Idea, "x", "R", At: new Point(1, 2)))).Code);
        Assert.Equal(ErrorCodes.ForbiddenForActor, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new UnpinIntent([id]))).Code);
    }

    [Fact]
    public void Reorganizing_refuses_to_move_pinned_cards_unless_told_to_skip_them()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("Source", "pinned"), Note("Source", "free"), new AddRegionIntent("Target"));
        var pinned = canvas.Card("pinned");
        canvas.Run(Actor.Owner, new SetLayoutIntent([new LayoutItem(pinned.Id, pinned.X, pinned.Y)]));
        var move = new MoveCardsIntent([pinned.Id, canvas.Card("free").Id], "Target");

        var error = Assert.Throws<SprawlException>(() => canvas.Run(Mcp, move));
        Assert.Equal(ErrorCodes.PinnedItem, error.Code);
        Assert.Equal([pinned.Id], error.Entities);

        var result = canvas.Run(new BatchRequest([move], SkipPinned: true), Mcp);
        Assert.Contains(result.Skipped, item => item.Reason == "pinned" && item.Id == pinned.Id);
        Assert.Equal(canvas.Region("Target").Id, canvas.Card("free").RegionId);
        Assert.Equal(canvas.Region("Source").Id, canvas.Card("pinned").RegionId);
    }

    [Fact]
    public void Merging_regions_moves_cards_rewires_connections_and_removes_the_source()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("Votes", "v1", reference: "v1"), Note("Votes", "v2"), Note("Elections", "e1", reference: "e1"), new ConnectIntent("v1", "e1"));
        var votes = canvas.Region("Votes").Id;
        canvas.Run(Mcp, new ConnectIntent(votes, canvas.Card("e1").Id));

        canvas.Run(Mcp, new MergeRegionsIntent([votes], "Elections"));

        Assert.DoesNotContain(votes, canvas.State.Regions.Keys);
        Assert.All(canvas.State.Cards.Values, card => Assert.Equal(canvas.Region("Elections").Id, card.RegionId));
        // The region-to-card edge would have become a region-to-own-card edge; the card-to-card edge survives.
        Assert.Equal(2, canvas.State.Connections.Count);
        Assert.Empty(StateOps.IntegrityProblems(canvas.State));
    }

    [Fact]
    public void Merging_cards_folds_bodies_and_rewires_edges_without_duplicates()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp,
            new AddCardIntent(CardKinds.Idea, "Leader lease", "R", "a", "Leaders hold a lease."),
            new AddCardIntent(CardKinds.Idea, "Lease-based reads", "R", "b", "Reads skip the log."),
            new AddCardIntent(CardKinds.Question, "Clock drift?", "R", "q"),
            new ConnectIntent("a", "q"), new ConnectIntent("b", "q"), new ConnectIntent("a", "b"));

        canvas.Run(Mcp, new MergeCardsIntent([canvas.Card("Lease-based reads").Id], canvas.Card("Leader lease").Id));

        var merged = canvas.Card("Leader lease");
        Assert.Contains("Leaders hold a lease.", merged.Body);
        Assert.Contains("**Lease-based reads**", merged.Body);
        Assert.Equal(2, canvas.State.Cards.Count);
        var edge = Assert.Single(canvas.State.Connections.Values);
        Assert.Equal((merged.Id, canvas.Card("Clock drift?").Id), (edge.SourceId, edge.TargetId));
    }

    [Fact]
    public void Connecting_twice_is_a_no_op()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("R", "a", reference: "a"), Note("R", "b", reference: "b"), new ConnectIntent("a", "b"));
        var again = canvas.Run(Mcp, new ConnectIntent(canvas.Card("a").Id, canvas.Card("b").Id));
        Assert.Empty(again.Ops);
        Assert.Equal("No changes", again.Summary);
    }

    [Fact]
    public void Validation_rejects_bad_input_with_specific_messages()
    {
        var canvas = new Sandbox();
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddCardIntent(CardKinds.Link, "No url", "R"))).Code);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddCardIntent("essay", "x", "R"))).Code);
        Assert.Equal(ErrorCodes.LimitExceeded, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddCardIntent(CardKinds.Note, new string('t', 121), "R"))).Code);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddCardIntent(CardKinds.Link, "Bad", "R", Url: "javascript:alert(1)"))).Code);
        Assert.Equal(ErrorCodes.NotFound, Assert.Throws<SprawlException>(() => canvas.Run(new BatchRequest([Note("Nowhere", "x")], CreateMissingRegions: false), Mcp)).Code);
    }

    [Fact]
    public void Canvas_details_normalize_tags()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new UpdateCanvasIntent(Tags: ["Distributed Systems", "papers", "papers"], Collection: "Learning", Pinned: true));
        Assert.Equal(["distributed-systems", "papers"], canvas.State.Meta!.Tags);
        Assert.Equal("Learning", canvas.State.Meta.Collection);
        Assert.True(canvas.State.Meta.Pinned);
    }
}
