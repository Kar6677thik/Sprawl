using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sprawl.Mcp.Tests;

public sealed class AuthTests(McpHost host) : IClassFixture<McpHost>
{
    private static readonly string[] ReadTools = ["analyze_canvas", "get_canvas", "get_card_context", "get_cards", "get_due_questions", "get_history", "list_canvases", "search"];
    private static readonly string[] AllTools =
    [
        "add_to_canvas", "analyze_canvas", "connect", "create_canvas", "delete_canvas", "delete_cards", "disconnect", "get_canvas", "get_card_context", "get_cards", "get_due_questions", "get_history",
        "list_canvases", "reorganize", "revert_canvas", "search", "update_canvas", "update_cards"
    ];

    [Fact]
    public async Task Anonymous_requests_get_401_pointing_at_the_protected_resource_metadata()
    {
        var (status, _, response) = await Rpc.Send(host.Client(), null, "tools/list");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("resource_metadata=\"https://canvas-mcp.test/.well-known/oauth-protected-resource\"", challenge);
        Assert.Contains("scope=\"canvas.read canvas.write canvas.organize\"", challenge);
        Assert.DoesNotContain("canvas.admin", challenge);
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    public async Task Protected_resource_metadata_names_exactly_one_authorization_server(string path)
    {
        var metadata = await host.Client().GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(McpHost.Resource, metadata.GetProperty("resource").GetString());
        Assert.Equal([McpHost.Issuer], metadata.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["canvas.read", "canvas.write", "canvas.organize", "canvas.admin"], metadata.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["header"], metadata.GetProperty("bearer_methods_supported").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Tool_discovery_shows_only_the_tools_the_granted_scopes_allow()
    {
        var all = await Rpc.Send(host.Client(), McpHost.Token("canvas.read canvas.write canvas.organize canvas.admin"), "tools/list");
        Assert.Equal(AllTools, Rpc.ToolNames(all.Body));

        var read = await Rpc.Send(host.Client(), McpHost.Token("canvas.read"), "tools/list");
        Assert.Equal(ReadTools, Rpc.ToolNames(read.Body));

        // canvas.admin implies nothing else.
        var admin = await Rpc.Send(host.Client(), McpHost.Token("canvas.admin"), "tools/list");
        Assert.Equal(["delete_canvas", "delete_cards"], Rpc.ToolNames(admin.Body));
    }

    [Fact]
    public async Task Calling_a_tool_outside_the_granted_scopes_is_refused()
    {
        var (status, body, _) = await Rpc.Send(host.Client(), McpHost.Token("canvas.read"), "tools/call", new Dictionary<string, object?>
        {
            ["name"] = "create_canvas",
            ["arguments"] = new Dictionary<string, object?> { ["title"] = "Should not exist" }
        });
        var refused = body.TryGetProperty("error", out _) || (body.TryGetProperty("result", out var result) && result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        Assert.True(status != HttpStatusCode.OK || refused, body.ToString());
        var canvases = Rpc.Data(await Rpc.Call(host.Client(), McpHost.Token("canvas.read"), "list_canvases", new { query = "Should not exist" }));
        Assert.Equal(0, canvases.GetArrayLength());
    }

    public static TheoryData<string, string> BadTokens() => new()
    {
        { "wrong audience", McpHost.Token(audience: "https://mcp.karthiksurkanti.in/mcp") },
        { "wrong issuer", McpHost.Token(issuer: "https://evil.test/") },
        { "expired", McpHost.Token(expires: DateTime.UtcNow.AddMinutes(-5)) },
        { "plain JWT type, not an access token", McpHost.Token(type: "JWT") },
        { "HS256 instead of ES256", McpHost.Token(credentials: Keys.Hs256()) },
        { "signed by an unknown key", McpHost.Token(credentials: Keys.OtherEs256()) },
        { "another user", McpHost.Token(subject: "someone-else-1234") },
        { "a machine identity", McpHost.Token(subject: "machine@clients") },
    };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task Tokens_that_are_not_the_owners_access_tokens_for_this_resource_are_refused(string why, string token)
    {
        var (status, _, _) = await Rpc.Send(host.Client(), token, "tools/list");
        Assert.True(status == HttpStatusCode.Unauthorized, why);
    }

    [Fact]
    public async Task Requests_from_unknown_browser_origins_are_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }) };
        request.Headers.Authorization = new("Bearer", McpHost.Token());
        request.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client().SendAsync(request)).StatusCode);
    }
}

public sealed class StartupTests
{
    [Fact]
    public void Production_without_an_issuer_refuses_to_start_even_with_a_dev_bearer_configured()
    {
        using var host = new McpHost { EnvironmentName = "Production" };
        host.Settings["OAuth:Authority"] = "";
        host.Settings["OAuth:AuthorizationServers:0"] = "";
        host.Settings["Development:BearerToken"] = "dev-token";
        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Contains("OAuth:Authority is required", Flatten(error));
    }

    [Fact]
    public void Production_requires_the_audience_to_equal_the_resource_and_one_matching_authorization_server()
    {
        using var mismatch = new McpHost { EnvironmentName = "Production" };
        mismatch.Settings["OAuth:Audience"] = "https://other.test/mcp";
        Assert.Contains("must exactly match", Flatten(Assert.ThrowsAny<Exception>(() => mismatch.CreateClient())));

        using var servers = new McpHost { EnvironmentName = "Production" };
        servers.Settings["OAuth:AuthorizationServers:0"] = "https://auth.test";
        Assert.Contains("exactly the issuer", Flatten(Assert.ThrowsAny<Exception>(() => servers.CreateClient())));
    }

    [Fact]
    public async Task In_production_the_dev_bearer_is_never_accepted()
    {
        using var host = new McpHost { EnvironmentName = "Production" };
        host.Settings["Development:BearerToken"] = "dev-token";
        var (status, _, _) = await Rpc.Send(host.Client(), "dev-token", "tools/list");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        var (ok, _, _) = await Rpc.Send(host.Client(), McpHost.Token(), "tools/list");
        Assert.Equal(HttpStatusCode.OK, ok);
    }

    [Fact]
    public async Task In_development_without_an_issuer_the_dev_bearer_works_and_nothing_else_does()
    {
        using var host = new McpHost { EnvironmentName = "Development" };
        host.Settings["OAuth:Authority"] = "";
        host.Settings["OAuth:AuthorizationServers:0"] = "";
        host.Settings["Development:BearerToken"] = "dev-token-for-tests";
        Assert.Equal(HttpStatusCode.OK, (await Rpc.Send(host.Client(), "dev-token-for-tests", "tools/list")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Rpc.Send(host.Client(), McpHost.Token(), "tools/list")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Rpc.Send(host.Client(), "wrong", "tools/list")).Status);
    }

    private static string Flatten(Exception error)
    {
        var messages = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException) messages.Add(current.Message);
        if (error is AggregateException aggregate) messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
        return string.Join(" | ", messages);
    }
}
