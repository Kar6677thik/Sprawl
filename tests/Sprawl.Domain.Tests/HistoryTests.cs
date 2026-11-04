using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void Replaying_the_op_log_from_nothing_rebuilds_the_current_state()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("A", "one", 50), Note("B", "two", 700));
        canvas.Run(Mcp, new ConnectIntent(EntityIdsOf(canvas, "A"), EntityIdsOf(canvas, "B")));
        canvas.Run(Mcp, new UpdateCardIntent(canvas.Card("one").Id, Body: new string('q', 900)));
        canvas.Run(Mcp, new DeleteCardsIntent([canvas.Card("two").Id]));

        var rebuilt = canvas.History.Aggregate(new CanvasState(), (state, ops) => HistoryEngine.Replay(state, ops));
        Assert.Empty(StateOps.Diff(canvas.State, rebuilt));
    }

    [Fact]
    public void Undo_restores_exactly_the_state_before_the_batch()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("A", "first"));
        var before = canvas.State.Clone();
        var batch = canvas.Run(Mcp, Note("A", "second", 400), Note("B", "third"), new UpdateCanvasIntent(Title: "Renamed"));

        canvas.Apply(HistoryEngine.Undo(canvas.State, batch.Ops));

        Assert.Empty(StateOps.Diff(before, canvas.State));
    }

    [Fact]
    public void Undo_refuses_when_a_later_batch_touched_the_same_items()
    {
        var canvas = new Sandbox();
        var batch = canvas.Run(Mcp, Note("A", "card"));
        canvas.Run(Mcp, new UpdateCardIntent(canvas.Card("card").Id, Title: "edited"));

        var error = Assert.Throws<SprawlException>(() => HistoryEngine.Undo(canvas.State, batch.Ops));
        Assert.Equal(ErrorCodes.Conflict, error.Code);
        Assert.Contains(canvas.Card("edited").Id, error.Entities);
    }

    [Fact]
    public void Undo_refuses_to_leave_later_connections_dangling()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("A", "old"));
        var batch = canvas.Run(Mcp, Note("B", "new"));
        // Connecting does not change either card, so only the integrity check can catch this.
        canvas.Run(Mcp, new ConnectIntent(canvas.Card("old").Id, canvas.Card("new").Id));

        var error = Assert.Throws<SprawlException>(() => HistoryEngine.Undo(canvas.State, batch.Ops));
        Assert.Equal(ErrorCodes.Conflict, error.Code);
    }

    [Fact]
    public void Undoing_a_tidy_puts_everything_back()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("A", "a"), Note("A", "b"), Note("B", "c"));
        var before = canvas.State.Clone();
        var items = canvas.State.Cards.Values.Select((card, i) => new LayoutItem(card.Id, 24, 48 + i * 200, AsAuto: true)).ToList();
        var tidy = canvas.Run(new BatchRequest([new SetLayoutIntent(items)], Kind: BatchKinds.Tidy), Actor.Owner);
        Assert.StartsWith("Tidied", tidy.Summary);

        canvas.Apply(HistoryEngine.Undo(canvas.State, tidy.Ops));

        Assert.Empty(StateOps.Diff(before, canvas.State));
    }

    [Fact]
    public void Revert_returns_to_an_earlier_version_and_can_itself_be_reverted()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, Note("A", "kept"));
        var version2 = canvas.State.Clone();
        canvas.Run(Mcp, Note("A", "later"), new UpdateCardIntent(canvas.Card("kept").Id, Title: "changed"));
        canvas.Run(Mcp, new DeleteCardsIntent([canvas.Card("later").Id]), new AddRegionIntent("Extra"));
        var latest = canvas.State.Clone();

        canvas.Apply(HistoryEngine.Revert(canvas.State, version2));
        Assert.Empty(StateOps.Diff(version2, canvas.State));
        Assert.NotNull(canvas.Card("kept")); // restored with the same id

        canvas.Apply(HistoryEngine.Revert(canvas.State, latest));
        Assert.Empty(StateOps.Diff(latest, canvas.State));
    }

    [Fact]
    public void Op_types_name_what_changed()
    {
        var canvas = new Sandbox();
        var add = canvas.Run(Mcp, Note("A", "card"));
        Assert.Contains(add.Ops, op => op.Type == "card.create");
        Assert.Contains(add.Ops, op => op.Type == "region.create");

        var card = canvas.Card("card");
        var move = canvas.Run(Actor.Owner, new SetLayoutIntent([new LayoutItem(card.Id, card.X + 300, card.Y)]));
        Assert.Contains(move.Ops, op => op.Type == "card.layout");

        var edit = canvas.Run(Mcp, new UpdateCardIntent(card.Id, Title: "renamed"));
        Assert.Contains(edit.Ops, op => op.Type == "card.update");
        Assert.All(edit.Ops, op => Assert.Equal(op, op.Inverse().Inverse()));
    }

    private static string EntityIdsOf(Sandbox canvas, string regionTitle) => canvas.Region(regionTitle).Id;
}
