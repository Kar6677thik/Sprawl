using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class SketchTests
{
    private const string Path = "M0 0 L40 10 Q60 20 80 0";

    [Fact]
    public void The_owner_draws_in_a_region_and_claude_only_sees_a_count()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddCardIntent(CardKinds.Idea, "Votes", "Election"));
        var result = canvas.Run(Actor.Owner, new AddSketchIntent(Path, 30, 60, 80, 20, "Election", "blue", 3, "s"));

        var sketch = Assert.Single(canvas.State.Sketches.Values);
        Assert.Equal((canvas.Region("Election").Id, "blue", 3, 30, 60, Authors.Human), (sketch.RegionId, sketch.Tint, sketch.Stroke, sketch.X, sketch.Y, sketch.CreatedBy));
        Assert.Equal(sketch.Id, result.Refs["s"]);
        Assert.StartsWith("sk_", sketch.Id);
        Assert.Contains("+1 sketch", result.Summary);
        Assert.Contains("(1 card, 1 sketch by the owner)", OutlineRenderer.Render(canvas.State, 3, new OutlineOptions()));
        Assert.DoesNotContain(Path, OutlineRenderer.Render(canvas.State, 3, new OutlineOptions(OutlineDepths.Full)));

        Assert.Equal(ErrorCodes.ForbiddenForActor, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddSketchIntent(Path, 0, 0, 10, 10))).Code);
        Assert.Equal(ErrorCodes.ForbiddenForActor, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new DeleteSketchesIntent([sketch.Id]))).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("L0 0")]
    [InlineData("M0 0 A 5 5 0 0 1 10 10")]
    [InlineData("M0 0 <script>")]
    [InlineData("M0 0 url(#x)")]
    public void Paths_are_limited_to_safe_drawing_commands(string path)
    {
        var canvas = new Sandbox();
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => canvas.Run(Actor.Owner, new AddSketchIntent(path, 0, 0, 10, 10))).Code);
    }

    [Fact]
    public void Sketches_move_by_drag_go_with_their_region_and_undo_like_anything_else()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddRegionIntent("A"), new AddRegionIntent("B"));
        canvas.Run(Actor.Owner, new AddSketchIntent(Path, 30, 60, 80, 20, "A"), new AddSketchIntent(Path, -400, -400, 80, 20));
        var inA = canvas.State.Sketches.Values.Single(s => s.RegionId is not null);
        var loose = canvas.State.Sketches.Values.Single(s => s.RegionId is null);

        canvas.Run(Actor.Owner, new SetLayoutIntent([new LayoutItem(loose.Id, 50, 70, Container: canvas.Region("B").Id)]));
        Assert.Equal((canvas.Region("B").Id, 50, 70), (canvas.State.Sketches[loose.Id].RegionId, canvas.State.Sketches[loose.Id].X, canvas.State.Sketches[loose.Id].Y));

        var before = canvas.State.Clone();
        var deleted = canvas.Run(Actor.Owner, new DeleteRegionIntent(canvas.Region("A").Id));
        Assert.False(canvas.State.Sketches.ContainsKey(inA.Id));
        Assert.Contains(deleted.Ops, op => op.Type == "sketch.delete" && op.EntityId == inA.Id);

        foreach (var op in Enumerable.Reverse(deleted.Ops)) StateOps.Apply(canvas.State, op.Inverse());
        Assert.Empty(StateOps.Diff(before, canvas.State));
        Assert.Empty(StateOps.IntegrityProblems(canvas.State));
    }

    [Fact]
    public void Snapshots_written_before_sketches_still_load()
    {
        var snapshot = new CanvasSnapshot(null, [], [], []);
        Assert.Empty(CanvasState.FromSnapshot(snapshot).Sketches);
    }
}
