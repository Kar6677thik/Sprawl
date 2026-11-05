using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class IconTests
{
    private const string Arch = "flowchart LR\n  user@{ shape: stadium, label: \"Shopper\", icon: \"fa:fa-user\" } --> api@{ shape: rect, label: \"API\", icon: \"gateway\" }\n" +
                                "  api --> db@{ shape: cyl, label: \"Orders DB\", icon: \"postgres\" }\n  api --> odd@{ shape: rect, label: \"Odd\", icon: \"fa:fa-unicorn\" }";

    [Theory]
    [InlineData("database", "database")]
    [InlineData("fa:fa-database", "database")]
    [InlineData("mdi:email", "mail")]
    [InlineData("Postgres", "database")]
    [InlineData("k8s", "container")]
    [InlineData("load balancer", "load-balancer")]
    public void Icon_names_are_read_the_way_models_write_them(string written, string icon) => Assert.Equal(icon, IconNames.Normalize(written));

    [Fact]
    public void Mermaid_icons_become_card_icons_and_survive_the_round_trip()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("Shop", Arch, "d"));
        Assert.Equal("user", canvas.State.Cards[result.Refs["d:user"]].Icon);
        Assert.Equal("api", canvas.State.Cards[result.Refs["d:api"]].Icon);
        Assert.Equal("database", canvas.State.Cards[result.Refs["d:db"]].Icon);
        Assert.Null(canvas.State.Cards[result.Refs["d:odd"]].Icon); // unknown icons are dropped, not refused

        var mermaid = Mermaid.Export(canvas.State, canvas.Region("Shop"));
        Assert.Contains("@{ shape: cyl, label: \"Orders DB\", icon: \"database\" }", mermaid);
        var again = Mermaid.Parse(mermaid);
        Assert.Equal(["user", "api", "database", null], again.Nodes.Select(n => n.Icon));
        Assert.Equal(Shapes.Cylinder, again.Nodes[2].Shape);
    }

    [Fact]
    public void An_icon_makes_room_on_its_shape_is_drawn_by_the_server_and_shows_in_the_outline()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("Shop", "flowchart LR\n  a[API] --> b[API]", "d"));
        var (plain, iconic) = (canvas.State.Cards[result.Refs["d:a"]], canvas.State.Cards[result.Refs["d:b"]]);
        canvas.Run(Mcp, new UpdateCardIntent(iconic.Id, Icon: "server"));
        iconic = canvas.State.Cards[iconic.Id];
        Assert.Equal("server", iconic.Icon);
        Assert.True(iconic.H >= plain.H + ShapeGeometry.IconRow, $"{iconic.H} vs {plain.H}");

        var svg = SvgRenderer.Render(canvas.State, canvas.Region("Shop"));
        Assert.Contains("<g class=\"icon\"", svg);
        Assert.Contains(IconPaths.Markup["server"], svg);
        Assert.Contains("[rectangle] (icon server) API", OutlineRenderer.Render(canvas.State, 1, new OutlineOptions(OutlineDepths.Titles)));

        Assert.Throws<SprawlException>(() => canvas.Run(Mcp, new UpdateCardIntent(iconic.Id, Icon: "unicorn")));
        canvas.Run(Mcp, new UpdateCardIntent(iconic.Id, Icon: "none"));
        Assert.Null(canvas.State.Cards[iconic.Id].Icon);
        Assert.Equal(plain.H, canvas.State.Cards[iconic.Id].H);
    }

    [Fact]
    public void Every_icon_has_a_drawing() => Assert.All(Icons.All, name => Assert.True(IconPaths.Markup.ContainsKey(name), name));
}
