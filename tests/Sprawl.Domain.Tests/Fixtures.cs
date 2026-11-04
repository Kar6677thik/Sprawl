using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Domain.Tests;

/// <summary>Sequential ids so layouts are reproducible across runs.</summary>
public sealed class SequentialIds : IIdGenerator
{
    private int next;
    public string Next(EntityKind kind) => EntityIds.Prefix(kind) + (++next).ToString("D8");
}

/// <summary>A canvas plus the helpers tests use to apply batches the way the API does.</summary>
public sealed class Sandbox
{
    public static readonly Actor Mcp = new(ActorKinds.Mcp, "test-client");
    public CanvasState State { get; private set; }
    public SequentialIds Ids { get; } = new();
    public DateTimeOffset Now { get; set; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    public List<List<Op>> History { get; } = [];

    public Sandbox(string title = "Raft consensus")
    {
        var created = BatchEngine.Create("cv_00000001", new CreateCanvasRequest(title), Actor.Owner, Now, Ids);
        State = HistoryEngine.Replay(new CanvasState(), created.Ops);
        History.Add(created.Ops);
    }

    public EngineResult Run(Actor actor, params Intent[] intents) => Run(new BatchRequest(intents), actor);

    public EngineResult Run(BatchRequest request, Actor actor)
    {
        Now = Now.AddSeconds(1);
        var result = BatchEngine.Execute(State, request, actor, Now, Ids);
        State = HistoryEngine.Replay(State, result.Ops);
        History.Add(result.Ops);
        return result;
    }

    public void Apply(List<Op> ops)
    {
        State = HistoryEngine.Replay(State, ops);
        History.Add(ops);
    }

    public Card Card(string title) => State.Cards.Values.Single(card => card.Title == title);
    public Region Region(string title) => State.Regions.Values.Single(region => region.Title == title);

    public static AddCardIntent Note(string region, string title, int bodyLength = 0, string? reference = null) =>
        new(CardKinds.Note, title, region, reference, new string('x', bodyLength));
}
