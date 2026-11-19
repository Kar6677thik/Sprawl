using System.Net;

namespace Sprawl.Mcp.Tests;

/// <summary>Networking:TrustedProxies takes single addresses (cloudflared) and CIDR ranges (the Orbit Hub's pods); anything else stops startup.</summary>
public sealed class ProxyTests
{
    [Fact]
    public async Task Single_addresses_and_cidr_ranges_are_accepted()
    {
        using var host = new McpHost();
        host.Settings["Networking:TrustedProxies:0"] = "10.42.0.1";
        host.Settings["Networking:TrustedProxies:1"] = "10.42.0.0/16";
        var response = await host.Client().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("10.42.0.0/33")]
    [InlineData("10.42.0.0/")]
    [InlineData("hub")]
    [InlineData("10.42.0")]
    public void A_malformed_entry_stops_startup(string entry)
    {
        using var host = new McpHost();
        host.Settings["Networking:TrustedProxies:0"] = entry;
        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        var messages = new List<string>();
        for (Exception? e = error; e is not null; e = e.InnerException) messages.Add(e.Message);
        Assert.Contains("Networking:TrustedProxies", string.Join(" | ", messages));
    }
}
