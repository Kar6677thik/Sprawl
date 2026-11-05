using System.Xml.Linq;
using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class SvgRendererTests
{
    [Fact]
    public void A_diagram_draws_as_well_formed_svg_with_an_item_per_card_group_and_connection()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("Shop", """
            flowchart LR
              user@{ shape: person, label: "Shopper" } --> web[Web app]
              subgraph core [Core]
                api{{API}} ==>|calls| db[(Orders DB)]
              end
              web -.->|HTTPS| api
            """, "d"));

        var svg = SvgRenderer.Render(canvas.State, canvas.Region("Shop"));
        var doc = XDocument.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var items = doc.Descendants(ns + "g").Where(g => g.Attribute("data-id") is not null).ToList();
        Assert.Equal(canvas.State.Cards.Count + 1 + canvas.State.Connections.Count, items.Count);
        Assert.Contains(items, g => g.Attribute("data-id")!.Value == result.Refs["d:api"] && g.Attribute("data-group")!.Value == canvas.Region("Core").Id);
        var dashed = items.Single(g => g.Attribute("class")!.Value.Contains("line-dashed"));
        Assert.Equal((result.Refs["d:web"], result.Refs["d:api"]), (dashed.Attribute("data-from")!.Value, dashed.Attribute("data-to")!.Value));
        Assert.Contains("marker-end=\"url(#arrow)\"", svg);
        Assert.Contains(">HTTPS<", svg);
        Assert.Contains(">Orders DB<", svg);
    }

    [Fact]
    public void Untrusted_text_is_escaped_and_never_becomes_markup()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp, new AddCardIntent(CardKinds.Note, "<script>alert(1)</script>", "R & D", Body: "a \"quoted\" <img src=x onerror=alert(1)>"));
        var svg = SvgRenderer.Render(canvas.State, canvas.Region("R & D"));

        Assert.DoesNotContain("<script", svg);
        Assert.DoesNotContain("<img", svg);
        Assert.Contains("&lt;script&gt;", svg);
        Assert.Contains("R &amp; D", svg);
        XDocument.Parse(svg); // still well-formed
    }

    [Fact]
    public void Routes_leave_from_the_facing_sides_at_right_angles()
    {
        var (right, _, _) = SvgRenderer.Route(new("a", 0, 0, 100, 50), new("b", 300, 0, 100, 50));
        Assert.Equal("M100 25 H200 V25 H300", right);
        var (down, _, _) = SvgRenderer.Route(new("a", 0, 0, 100, 50), new("b", 0, 200, 100, 50));
        Assert.Equal("M50 50 V125 H50 V200", down);
    }
}
