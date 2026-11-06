using System.Text.Json;

namespace Sprawl.Mcp.Tests;

/// <summary>Addresses that open canvases and cards in the web app: present only when Mcp:WebAppUrl is set, and only a valid https origin.</summary>
public sealed class LinkTests
{
    private static readonly string Everyday = McpHost.Token();
    private const string WebApp = "https://sprawl.test";

    private static async Task<(string CanvasId, JsonElement Created, string Card, string Region)> Seed(HttpClient client, string title)
    {
        var created = Rpc.Data(await Rpc.Call(client, Everyday, "create_canvas", new
        {
            title,
            regions = new object[] { new { title = "Sources", @ref = "s" } },
            cards = new object[]
            {
                new { region = "s", kind = "link", title = "Replication notes", url = "https://notes.test/?note=n_42", @ref = "n" },
                new { region = "s", kind = "idea", title = "Synchronous replicas trade latency for safety", @ref = "i" },
            }
        }));
        var refs = created.GetProperty("refs");
        return (created.GetProperty("canvasId").GetString()!, created, refs.GetProperty("n").GetString()!, refs.GetProperty("s").GetString()!);
    }

    [Fact]
    public async Task With_the_web_app_configured_canvases_and_cards_carry_addresses_that_open_them()
    {
        using var host = new McpHost();
        host.Settings["Mcp:WebAppUrl"] = WebApp + "/";
        var client = host.Client();
        var (canvasId, created, card, region) = await Seed(client, "Replication linked");

        Assert.Equal($"{WebApp}/c/{canvasId}", created.GetProperty("url").GetString());
        Assert.Equal($"{WebApp}/c/{canvasId}?focus={card}", created.GetProperty("webUrls").GetProperty("n").GetString());
        Assert.Equal($"{WebApp}/c/{canvasId}?focus={region}", created.GetProperty("webUrls").GetProperty("s").GetString());

        var listed = Rpc.Data(await Rpc.Call(client, Everyday, "list_canvases", new { query = "Replication linked" }))[0];
        Assert.Equal($"{WebApp}/c/{canvasId}", listed.GetProperty("url").GetString());

        // A link card keeps its own url (where it points); webUrl is where it opens in Sprawl.
        var full = Rpc.Data(await Rpc.Call(client, Everyday, "get_cards", new { cardIds = new[] { card } }))[0];
        Assert.Equal("https://notes.test/?note=n_42", full.GetProperty("url").GetString());
        Assert.Equal($"{WebApp}/c/{canvasId}?focus={card}", full.GetProperty("webUrl").GetString());

        var found = Rpc.Data(await Rpc.Call(client, Everyday, "search", new { query = "synchronous replicas" }));
        Assert.StartsWith($"{WebApp}/c/{canvasId}?focus=cd_", found.GetProperty("cards")[0].GetProperty("webUrl").GetString());

        Assert.StartsWith($"url: {WebApp}/c/{canvasId}\n", Rpc.Text(await Rpc.Call(client, Everyday, "get_canvas", new { canvasId })));

        Rpc.Data(await Rpc.Call(client, Everyday, "add_diagram", new { canvasId, title = "Write path", mermaid = "flowchart LR\n  a[Primary] --> b[(Replica)]" }));
        var view = Rpc.Data(await Rpc.Call(client, Everyday, "show_diagram", new { canvasId, region = "Write path" }));
        Assert.Equal($"{WebApp}/c/{canvasId}?focus={view.GetProperty("regionId").GetString()}", view.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Without_the_web_app_configured_no_address_appears()
    {
        using var host = new McpHost();
        var client = host.Client();
        var (canvasId, created, card, _) = await Seed(client, "Replication unlinked");

        Assert.False(created.TryGetProperty("url", out _));
        Assert.False(created.TryGetProperty("webUrls", out _));
        Assert.False(Rpc.Data(await Rpc.Call(client, Everyday, "list_canvases", new { query = "Replication unlinked" }))[0].TryGetProperty("url", out _));
        var full = Rpc.Data(await Rpc.Call(client, Everyday, "get_cards", new { cardIds = new[] { card } }))[0];
        Assert.False(full.TryGetProperty("webUrl", out _));
        Assert.Equal("https://notes.test/?note=n_42", full.GetProperty("url").GetString());
        Assert.False(Rpc.Text(await Rpc.Call(client, Everyday, "get_canvas", new { canvasId })).StartsWith("url:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://sprawl.test")]
    [InlineData("https://sprawl.test/app")]
    [InlineData("https://user:secret@sprawl.test")]
    [InlineData("https://sprawl.test/?next=1")]
    [InlineData("https://sprawl.test/#top")]
    [InlineData("sprawl.test")]
    [InlineData("javascript:alert(1)")]
    public void A_web_app_address_that_is_not_an_https_origin_stops_startup(string value)
    {
        using var host = new McpHost();
        host.Settings["Mcp:WebAppUrl"] = value;
        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Contains("Mcp:WebAppUrl", Flatten(error));
    }

    private static string Flatten(Exception error)
    {
        var messages = new List<string>();
        for (Exception? e = error; e is not null; e = e.InnerException) messages.Add(e.Message);
        return string.Join(" | ", messages);
    }
}
