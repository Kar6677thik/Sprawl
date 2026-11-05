using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class DiagramTests
{
    private const string Checkout = """
        flowchart TD
          %% an order flow
          start([Order placed]) --> valid{Card valid?}
          valid -->|yes| reserve[Reserve stock]
          valid -- no --> reject[/Email the customer/]
          reserve --> pay[[Charge card]] --> db[(Orders DB)]
          pay -.-> audit>Audit log]
          reserve & pay --> done((Done))
          classDef hot fill:#f00
          class valid hot
        """;

    [Fact]
    public void Mermaid_flowcharts_parse_into_shapes_edges_and_styles()
    {
        var chart = Mermaid.Parse(Checkout);

        Assert.Equal(RegionLayouts.FlowDown, chart.Layout);
        Assert.Equal(["start", "valid", "reserve", "reject", "pay", "db", "audit", "done"], chart.Nodes.Select(n => n.Id));
        Assert.Equal(
            [Shapes.Terminal, Shapes.Diamond, Shapes.Rectangle, Shapes.Parallelogram, Shapes.Subroutine, Shapes.Cylinder, Shapes.Rectangle, Shapes.Circle],
            chart.Nodes.Select(n => n.Shape));
        Assert.Equal("Card valid?", chart.Nodes[1].Label);
        Assert.Contains(chart.Edges, e => e is { From: "valid", To: "reserve", Label: "yes", Line: LineStyles.Solid, Ends: EndStyles.End });
        Assert.Contains(chart.Edges, e => e is { From: "valid", To: "reject", Label: "no" });
        Assert.Contains(chart.Edges, e => e is { From: "pay", To: "audit", Line: LineStyles.Dashed });
        Assert.Contains(chart.Edges, e => e is { From: "pay", To: "db" }); // chains
        Assert.Equal(2, chart.Edges.Count(e => e.To == "done"));          // & fans in
    }

    [Theory]
    [InlineData("graph LR\n a --- b", RegionLayouts.FlowRight, EndStyles.None, LineStyles.Solid)]
    [InlineData("flowchart RL\n a <--> b", RegionLayouts.FlowRight, EndStyles.Both, LineStyles.Solid)]
    [InlineData("flowchart TB\n a ==> b", RegionLayouts.FlowDown, EndStyles.End, LineStyles.Thick)]
    [InlineData("flowchart TD\n a -. maybe .-> b", RegionLayouts.FlowDown, EndStyles.End, LineStyles.Dashed)]
    [InlineData("flowchart TD\n a ----> b", RegionLayouts.FlowDown, EndStyles.End, LineStyles.Solid)]
    [InlineData("flowchart TD\n a --o b", RegionLayouts.FlowDown, EndStyles.End, LineStyles.Solid)]
    public void Every_arrow_form_reads(string source, string layout, string ends, string line)
    {
        var chart = Mermaid.Parse(source);
        Assert.Equal(layout, chart.Layout);
        var edge = Assert.Single(chart.Edges);
        Assert.Equal((ends, line), (edge.Ends, edge.Line));
    }

    [Fact]
    public void Subgraphs_named_shapes_and_quoted_labels_read()
    {
        var chart = Mermaid.Parse("""
            flowchart LR
              subgraph api [Public API]
                direction TB
                gw["Gateway (edge)"] --> svc@{ shape: hex, label: "Orders service" }
                subgraph inner
                  cache[(Redis)]
                end
              end
              svc --> q@{ shape: h-cyl, label: Events }
              user@{ shape: person, label: Shopper } --> gw
              q --> api
            """);

        Assert.Equal([("api", "Public API", RegionLayouts.FlowDown)], chart.Subgraphs.Select(s => (s.Id, s.Title, s.Layout)));
        Assert.Equal("Gateway (edge)", chart.Nodes.Single(n => n.Id == "gw").Label);
        Assert.Equal((Shapes.Hexagon, "Orders service", "api"), chart.Nodes.Single(n => n.Id == "svc") is var s ? (s.Shape, s.Label, s.Subgraph) : default);
        Assert.Equal("api", chart.Nodes.Single(n => n.Id == "cache").Subgraph); // deeper subgraphs fold into the outer one
        Assert.Equal(Shapes.Queue, chart.Nodes.Single(n => n.Id == "q").Shape);
        Assert.Equal(Shapes.Person, chart.Nodes.Single(n => n.Id == "user").Shape);
        Assert.Contains(chart.Edges, e => e is { From: "q", To: "api" });
        Assert.DoesNotContain(chart.Nodes, n => n.Id == "api");
    }

    [Theory]
    [InlineData("sequenceDiagram\n A->>B: hi")]
    [InlineData("flowchart TD\n a --> ")]
    [InlineData("flowchart TD\n a[unclosed --> b")]
    public void Unsupported_or_broken_mermaid_is_refused_with_a_reason(string source) =>
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => Mermaid.Parse(source)).Code);

    [Fact]
    public void A_diagram_lands_laid_out_top_down_with_no_overlaps()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("Checkout flow", Checkout, "co"));

        var region = canvas.Region("Checkout flow");
        Assert.Equal(RegionLayouts.FlowDown, region.Layout);
        var cards = canvas.State.CardsIn(region.Id).ToList();
        Assert.Equal(8, cards.Count);
        Assert.Equal(Shapes.Diamond, canvas.State.Cards[result.Refs["co:valid"]].Shape);
        Assert.Equal(8, canvas.State.Connections.Count);

        // Every arrow points down the page: its target starts below its source.
        Card At(string id) => canvas.State.Cards[result.Refs["co:" + id]];
        foreach (var (from, to) in new[] { ("start", "valid"), ("valid", "reserve"), ("reserve", "pay"), ("pay", "db"), ("pay", "done") })
            Assert.True(At(to).Y >= At(from).Y + At(from).H, $"{to} should be below {from}");
        // Nothing overlaps, and everything sits inside the region.
        for (var i = 0; i < cards.Count; i++)
        {
            Assert.True(cards[i].X >= Geometry.Padding && cards[i].Y >= Geometry.Header && cards[i].X + cards[i].W <= region.W && cards[i].Y + cards[i].H <= region.H);
            for (var j = i + 1; j < cards.Count; j++)
                Assert.False(cards[i].X < cards[j].X + cards[j].W && cards[j].X < cards[i].X + cards[i].W && cards[i].Y < cards[j].Y + cards[j].H && cards[j].Y < cards[i].Y + cards[i].H,
                    $"{cards[i].Title} overlaps {cards[j].Title}");
        }
        // Deterministic: laying out the same chart again lands the same way.
        var again = new Sandbox();
        again.Run(Mcp, new AddDiagramIntent("Checkout flow", Checkout, "co"));
        Assert.Equal(cards.Select(c => (c.Title, c.X, c.Y)), again.State.CardsIn(again.Region("Checkout flow").Id).Select(c => (c.Title, c.X, c.Y)));
        Assert.Empty(StateOps.IntegrityProblems(canvas.State));
    }

    [Fact]
    public void Flow_right_runs_left_to_right_and_subgraphs_become_laid_out_sub_regions()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("Architecture", """
            flowchart LR
              user((User)) --> cdn[CDN]
              subgraph backend [Backend]
                api[API] --> db[(Postgres)]
              end
              cdn --> api
            """, "a"));

        var backend = canvas.Region("Backend");
        Assert.Equal(canvas.Region("Architecture").Id, backend.ParentId);
        Assert.Equal(RegionLayouts.FlowRight, backend.Layout);
        Card At(string id) => canvas.State.Cards[result.Refs["a:" + id]];
        Assert.True(At("cdn").X > At("user").X + At("user").W);
        Assert.True(backend.X > At("cdn").X + At("cdn").W, "the Backend group comes after the CDN");
        Assert.True(At("db").X > At("api").X + At("api").W);
        Assert.Equal(backend.Id, At("api").RegionId);
    }

    [Fact]
    public void Cycles_isolated_nodes_and_new_nodes_keep_the_layout_sound()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("Loop", "flowchart TD\n a --> b --> c --> a\n lone[Alone]", "l"));
        var region = canvas.Region("Loop");
        Assert.Equal(4, canvas.State.CardsIn(region.Id).Count());

        // Adding a node by hand-made intents joins the same diagram and is laid out with it.
        canvas.Run(Mcp, new AddCardIntent(CardKinds.Note, "Next", region.Id, "n", Shape: Shapes.Rounded), new ConnectIntent(result.Refs["l:c"], "n", ConnectionKinds.LeadsTo));
        var next = canvas.Card("Next");
        var c = canvas.State.Cards[result.Refs["l:c"]];
        Assert.True(next.Y >= c.Y + c.H);
        Assert.Equal(ShapeGeometry.Size(Shapes.Rounded, "Next"), (next.W, next.H));
    }

    [Fact]
    public void Connections_take_styles_and_restyle_in_place()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddCardIntent(CardKinds.Idea, "A", "R", "a"), new AddCardIntent(CardKinds.Idea, "B", "R", "b"),
            new ConnectIntent("a", "b", ConnectionKinds.DependsOn, "calls", LineStyles.Dashed, EndStyles.Both));
        var connection = Assert.Single(canvas.State.Connections.Values);
        Assert.Equal((LineStyles.Dashed, EndStyles.Both, "calls"), (connection.Line, connection.Ends, connection.Label));

        canvas.Run(Mcp, new UpdateConnectionIntent(connection.Id, Kind: ConnectionKinds.Relates, Label: "", Line: LineStyles.Solid, Ends: EndStyles.None));
        Assert.Equal((ConnectionKinds.Relates, (string?)null, (string?)null, EndStyles.None), canvas.State.Connections[connection.Id] is var c ? (c.Kind, c.Label, c.Line, c.Ends) : default);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new ConnectIntent(result.Refs["a"], result.Refs["b"], Line: "wavy"))).Code);
    }

    [Fact]
    public void Switching_a_region_between_cards_and_diagram_resizes_and_replaces_its_cards()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddCardIntent(CardKinds.Idea, "Decide", "R", "d", Shape: Shapes.Diamond), new AddCardIntent(CardKinds.Idea, "Act", "R", "a"),
            new ConnectIntent("d", "a", ConnectionKinds.LeadsTo));
        Assert.Equal(Geometry.CardWidth, canvas.Card("Decide").W); // a shape in a card region keeps the column width

        canvas.Run(Mcp, new UpdateRegionIntent(canvas.Region("R").Id, Layout: RegionLayouts.FlowDown));
        Assert.Equal(ShapeGeometry.Size(Shapes.Diamond, "Decide"), (canvas.Card("Decide").W, canvas.Card("Decide").H));
        Assert.True(canvas.Card("Act").Y > canvas.Card("Decide").Y + canvas.Card("Decide").H);

        canvas.Run(Mcp, new UpdateRegionIntent(canvas.Region("R").Id, Layout: RegionLayouts.Cards));
        Assert.Null(canvas.Region("R").Layout);
        Assert.Equal(Geometry.CardWidth, canvas.Card("Decide").W);
        Assert.Equal(Geometry.ColumnX(0), canvas.Card("Decide").X);
    }

    [Fact]
    public void A_diagram_exports_back_to_mermaid_that_reimports_to_the_same_shape()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddDiagramIntent("Checkout flow", Checkout, "co"));
        var exported = Mermaid.Export(canvas.State, canvas.Region("Checkout flow"));

        Assert.StartsWith("flowchart TD\n", exported);
        Assert.Contains("{\"Card valid?\"}", exported);
        Assert.Contains("-.->", exported);
        Assert.Contains("|\"yes\"|", exported);

        var copy = Mermaid.Parse(exported);
        var original = Mermaid.Parse(Checkout);
        Assert.Equal(original.Nodes.Select(n => (n.Label, n.Shape == Shapes.Rectangle && n.Id == "audit" ? Shapes.Rectangle : n.Shape)).OrderBy(x => x.Label),
            copy.Nodes.Select(n => (n.Label, n.Shape)).OrderBy(x => x.Label));
        Assert.Equal(original.Edges.Count, copy.Edges.Count);
    }

    [Fact]
    public void The_outline_tells_claude_which_regions_are_diagrams_and_which_cards_are_shapes()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddDiagramIntent("Checkout flow", Checkout, "co"));
        var outline = OutlineRenderer.Render(canvas.State, 2, new OutlineOptions());
        Assert.Contains("diagram, flows down", outline);
        Assert.Contains("[diamond] Card valid?", outline);
        Assert.Contains("[dashed]", outline);
    }

    private const string Login = """
        sequenceDiagram
          participant B as Browser
          participant A as API
          participant I as Identity
          B->>A: GET /orders
          A-->>B: 401 + resource_metadata
          B->>I: authorize (PKCE)
          I-->>B: code
          B->>I: token
          I-->>B: access token
          B->>A: GET /orders (Bearer)
          A-->>B: 200 orders
        """;

    [Theory]
    [InlineData("flowchart LR\n a --> b", "flowchart")]
    [InlineData("graph TD\n a --> b", "flowchart")]
    [InlineData("sequenceDiagram\n A->>B: hi", "sequenceDiagram")]
    [InlineData("%% comment\n\nerDiagram\n A ||--o{ B : has", "erDiagram")]
    [InlineData("---\ntitle: Plan\n---\ngantt\n title X", "gantt")]
    [InlineData("stateDiagram-v2\n [*] --> On", "stateDiagram-v2")]
    [InlineData("mindmap\n  root((Raft))", "mindmap")]
    [InlineData("architecture-beta\n service api(server)[API]", "architecture-beta")]
    public void Every_mermaid_type_is_recognised(string source, string type) => Assert.Equal(type, Mermaid.DiagramType(source));

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("<script>alert(1)</script>")]
    public void Text_that_is_not_mermaid_is_refused(string source) =>
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => Mermaid.DiagramType(source)).Code);

    [Fact]
    public void Any_other_mermaid_type_becomes_one_rendered_card_sized_from_its_source()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("OAuth login", Login, "l"));

        var region = canvas.Region("OAuth login");
        var card = Assert.Single(canvas.State.CardsIn(region.Id));
        Assert.Equal(result.Refs["l:diagram"], card.Id);
        Assert.Equal((CardKinds.Mermaid, "OAuth login"), (card.Kind, card.Title));
        Assert.StartsWith("sequenceDiagram", card.Body);
        Assert.Equal(MermaidGeometry.Size(Login), (card.W, card.H));
        Assert.True(card.W >= 3 * 170 && card.H >= 8 * 46, "three participants and eight messages need room");
        Assert.True(region.W >= card.W + 2 * Geometry.Padding);
        Assert.Equal(Login.Trim() + "\n", Mermaid.Export(canvas.State, region));
        Assert.Contains("mermaid OAuth login", OutlineRenderer.Render(canvas.State, 2, new OutlineOptions()));
    }

    [Fact]
    public void A_diagram_can_be_redrawn_as_a_different_type()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddDiagramIntent("Login", "flowchart LR\n b[Browser] --> a[API]", "l"));
        canvas.Run(Mcp, new AddDiagramIntent("Login", Login, "l2", Replace: true));

        var card = Assert.Single(canvas.State.CardsIn(canvas.Region("Login").Id));
        Assert.Equal(CardKinds.Mermaid, card.Kind);
        Assert.Empty(canvas.State.Connections);

        canvas.Run(Mcp, new AddDiagramIntent("Login", "flowchart TD\n b[Browser] --> a[API]", "l3", Replace: true));
        Assert.Equal(2, canvas.State.CardsIn(canvas.Region("Login").Id).Count(c => c.Shape is not null));
    }
}
