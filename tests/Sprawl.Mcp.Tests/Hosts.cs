extern alias SprawlApi;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Sprawl.Mcp.Clients;
using ApiProgram = SprawlApi::Program;
using DeviceTokens = SprawlApi::Sprawl.Api.Auth.DeviceTokens;

namespace Sprawl.Mcp.Tests;

/// <summary>The real Sprawl API in-process, temporary database, accepting one device token on its device listener.</summary>
public sealed class ApiHost : WebApplicationFactory<ApiProgram>
{
    public const int DevicePort = 9081;
    public const string DeviceToken = "spr_dev_mcp-test-device-token-0123456789abcdef";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sprawl-mcp-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.UseEnvironment("Testing");
        builder.UseSetting("Api:DatabasePath", Path.Combine(directory, "sprawl.db"));
        builder.UseSetting("Api:DevicePort", DevicePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Api:PublicOrigin", "https://localhost");
        builder.UseSetting("Auth:DeviceTokenSha256:0", DeviceTokens.Hash(DeviceToken));
        builder.UseSetting("AllowedHosts", "*");
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, DevicePortFilter>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>TestServer has no sockets: every request in this host arrives on the device listener.</summary>
    private sealed class DevicePortFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (HttpContext context, RequestDelegate nextMiddleware) =>
            {
                context.Connection.LocalPort = DevicePort;
                await nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>
/// Sprawl.Mcp as deployed: JwtBearer against a (static, in-memory) Orbit Identity issuer with an ES256 key, calling the
/// in-process API with the device token. Logs are captured so tests can prove no token is ever written.
/// </summary>
public class McpHost : WebApplicationFactory<Program>
{
    public const string Issuer = "https://auth.test/";
    public const string Resource = "https://canvas-mcp.test/mcp";
    public const string Owner = "owner-0b7d4f3c";
    public const string ClientId = "https://claude.ai/oauth/test-client";
    public static readonly ECDsaSecurityKey SigningKey = new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "test-es256" };
    public ApiHost Api { get; } = new();
    public ConcurrentQueue<string> Logs { get; } = new();
    public string EnvironmentName { get; init; } = "Testing";
    public Dictionary<string, string?> Settings { get; } = [];

    public static string Token(string scopes = "canvas.read canvas.write canvas.organize", string subject = Owner, string audience = Resource, string issuer = Issuer,
        string type = "at+jwt", SigningCredentials? credentials = null, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var expiry = expires ?? now.AddMinutes(10);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("scope", scopes), new Claim("client_id", ClientId)]),
            IssuedAt = expiry < now ? expiry.AddMinutes(-10) : now.AddSeconds(-5),
            NotBefore = expiry < now ? expiry.AddMinutes(-10) : now.AddSeconds(-5),
            Expires = expiry,
            TokenType = type,
            SigningCredentials = credentials ?? new SigningCredentials(SigningKey, SecurityAlgorithms.EcdsaSha256)
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        var settings = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Mcp:PublicOrigin"] = "https://canvas-mcp.test",
            ["Mcp:ApiBaseUrl"] = "http://api.test",
            ["Mcp:AllowedApiHosts:0"] = "api.test",
            ["OAuth:Authority"] = Issuer,
            ["OAuth:AuthorizationServers:0"] = Issuer,
            ["OAuth:Audience"] = Resource,
            ["OAuth:Resource"] = Resource,
            ["OAuth:AllowedSubjectIds:0"] = Owner,
            ["Secrets:ApiDeviceToken"] = ApiHost.DeviceToken,
        };
        foreach (var (key, value) in Settings) settings[key] = value;
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        builder.ConfigureLogging(logging => logging.AddProvider(new CaptureProvider(Logs)));
        builder.ConfigureTestServices(services =>
        {
            var oidc = new OpenIdConnectConfiguration { Issuer = Issuer };
            oidc.SigningKeys.Add(SigningKey);
            services.PostConfigure<JwtBearerOptions>("SprawlBearer", options => options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc));
            services.AddHttpClient<SprawlApiClient>().ConfigurePrimaryHttpMessageHandler(() => Api.Server.CreateHandler());
        });
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://canvas-mcp.test") });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Api.Dispose();
    }

    private sealed class CaptureProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capture(sink);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception));
        }
    }
}

/// <summary>JSON-RPC over the stateless Streamable HTTP transport, as the 2026-07-28 revision sends it.</summary>
public static class Rpc
{
    public static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Send(HttpClient client, string? token, string method, Dictionary<string, object?>? parameters = null)
    {
        parameters ??= [];
        parameters["_meta"] = new Dictionary<string, object?>
        {
            ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
            ["io.modelcontextprotocol/clientCapabilities"] = new Dictionary<string, object?>()
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters })
        };
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        if (parameters.TryGetValue("name", out var name) && name is string toolName) request.Headers.TryAddWithoutValidation("Mcp-Name", toolName);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (text.StartsWith("event:", StringComparison.Ordinal) || text.StartsWith("data:", StringComparison.Ordinal))
            text = text.Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))["data:".Length..].Trim();
        var body = text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, body, response);
    }

    /// <summary>Call a tool and return its structured result (the McpResult envelope) or, for text tools, the text.</summary>
    public static async Task<JsonElement> Call(HttpClient client, string token, string tool, object arguments)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments, SprawlApiClient.Json));
        var (status, body, _) = await Send(client, token, "tools/call", new Dictionary<string, object?> { ["name"] = tool, ["arguments"] = args });
        Assert.True(status == HttpStatusCode.OK, $"{tool}: HTTP {(int)status} {body}");
        Assert.True(body.TryGetProperty("result", out var result), $"{tool}: {body}");
        return result;
    }

    public static JsonElement Data(JsonElement result)
    {
        var structured = result.GetProperty("structuredContent");
        Assert.True(structured.GetProperty("ok").GetBoolean(), structured.ToString());
        return structured.GetProperty("data");
    }

    public static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;

    public static string[] ToolNames(JsonElement body) =>
        [.. body.GetProperty("result").GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!).Order(StringComparer.Ordinal)];
}

public static class Keys
{
    public static SigningCredentials Hs256() => new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-symmetric-key-that-is-long-enough-for-hs256")), SecurityAlgorithms.HmacSha256);
    public static SigningCredentials OtherEs256() => new(new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "test-es256" }, SecurityAlgorithms.EcdsaSha256);
}
