using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class ContentTests
{
    private const string Comparison = """
        | Feature | Abstract class | Interface |
        |---|---|---|
        | State | Can hold fields | No instance state (C++: pure virtuals only) |
        | Constructors | Yes | No |
        | Multiple inheritance | One base (Java/C#) | Many |
        """;

    private const string Latency = """
        {"$schema": "https://vega.github.io/schema/vega-lite/v6.json", "width": 520, "height": 260,
         "data": {"values": [{"p": "p50", "ms": 12}, {"p": "p95", "ms": 48}, {"p": "p99", "ms": 140}]},
         "mark": "bar", "encoding": {"x": {"field": "p", "type": "nominal"}, "y": {"field": "ms", "type": "quantitative"}}}
        """;

    [Fact]
    public void Tables_and_charts_span_columns_in_a_card_region_and_push_nothing_into_overlap()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp,
            new AddCardIntent(CardKinds.Idea, "Small", "R"),
            new AddCardIntent(CardKinds.Table, "Abstract class vs interface", "R", Body: Comparison),
            new AddCardIntent(CardKinds.Chart, "Latency percentiles", "R", Body: Latency),
            new AddCardIntent(CardKinds.Idea, "After", "R"));

        var table = canvas.Card("Abstract class vs interface");
        var chart = canvas.Card("Latency percentiles");
        Assert.True(ContentGeometry.SpanOf(table.W) >= 2, "a three-column table needs more than one card column");
        Assert.Equal(0, (table.W + Geometry.Gap) % Geometry.Pitch);
        Assert.Equal(ContentGeometry.SpanWidth(ContentGeometry.Chart(Latency).W), chart.W);
        Assert.True(canvas.Region("R").Cols >= ContentGeometry.SpanOf(chart.W));
        var cards = canvas.State.CardsIn(canvas.Region("R").Id).ToList();
        for (var i = 0; i < cards.Count; i++)
            for (var j = i + 1; j < cards.Count; j++)
                Assert.False(cards[i].X < cards[j].X + cards[j].W && cards[j].X < cards[i].X + cards[i].W && cards[i].Y < cards[j].Y + cards[j].H && cards[j].Y < cards[i].Y + cards[i].H,
                    $"{cards[i].Title} overlaps {cards[j].Title}");
        Assert.Contains("table Abstract class vs interface", OutlineRenderer.Render(canvas.State, 2, new OutlineOptions()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{"data": {"values": []}}""")]
    [InlineData("""{"mark": "bar", "data": {"url": "https://evil.example/data.csv"}}""")]
    [InlineData("""{"layer": [{"mark": "line", "data": {"URL": "data.json"}}]}""")]
    public void Charts_must_be_vega_lite_with_inline_data(string body)
    {
        var canvas = new Sandbox();
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new AddCardIntent(CardKinds.Chart, "Bad", "R", Body: body))).Code);
    }

    [Fact]
    public void A_diagram_can_be_the_detail_of_a_shape_and_the_link_goes_when_the_diagram_does()
    {
        var canvas = new Sandbox();
        var overview = canvas.Run(Mcp, new AddDiagramIntent("System", "flowchart LR\n  web[Web] --> api[API]", "s"));
        var api = overview.Refs["s:api"];
        canvas.Run(Mcp, new AddDiagramIntent("Inside the API", "flowchart TD\n  router[Router] --> handler[Handler] --> repo[(Repository)]", "i", DetailOf: api));

        var detail = canvas.Region("Inside the API");
        Assert.Equal(detail.Id, canvas.State.Cards[api].Detail);
        Assert.Contains($"[details: {detail.Id}]", OutlineRenderer.Render(canvas.State, 2, new OutlineOptions()));
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() =>
            canvas.Run(Mcp, new AddDiagramIntent("System", "flowchart LR\n  x --> y", "x", DetailOf: api))).Code); // its own shape

        canvas.Run(Mcp, new UpdateCardIntent(api, Detail: "none"));
        Assert.Null(canvas.State.Cards[api].Detail);
        canvas.Run(Mcp, new UpdateCardIntent(api, Detail: detail.Id));
        canvas.Run(Mcp, new DeleteCardsIntent([.. canvas.State.CardsIn(detail.Id).Select(c => c.Id)]), new DeleteRegionIntent(detail.Id));
        Assert.Null(canvas.State.Cards[api].Detail);
    }
}
