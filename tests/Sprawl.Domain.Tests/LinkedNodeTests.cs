using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class LinkedNodeTests
{
    [Fact]
    public void A_card_that_links_somewhere_carries_its_escaped_address_in_the_server_svg()
    {
        var canvas = new Sandbox();
        var result = canvas.Run(Mcp, new AddDiagramIntent("Sources", "flowchart LR\n  a[Notes] --> b[Plain]", "d"));
        canvas.Run(Mcp, new UpdateCardIntent(result.Refs["d:a"], Kind: CardKinds.Link, Url: "https://notes.test/?note=n_1&x=\"y\""));
        var svg = SvgRenderer.Render(canvas.State, canvas.Region("Sources"));

        Assert.Contains("data-href=\"https://notes.test/?note=n_1&amp;x=&quot;y&quot;\"", svg);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "data-href="));
    }
}
