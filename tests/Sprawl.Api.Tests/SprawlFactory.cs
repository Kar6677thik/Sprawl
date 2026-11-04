using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprawl.Api.Auth;
using Sprawl.Api.Data;

namespace Sprawl.Api.Tests;

/// <summary>A controllable clock so TOTP steps, session expiry and trash age can be tested.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// The real API in-process with a temporary database, a provisioned owner verifier and TOTP secret, and a device token.
/// TestServer has no real sockets, so a test-only startup filter sets the local port (which picks the listener) and a
/// loopback remote address from request headers.
/// </summary>
public class SprawlFactory : WebApplicationFactory<Program>
{
    public const int DevicePort = 9081;
    public const string Password = "correct horse battery staple";
    public const string DeviceToken = "spr_dev_test-token-0123456789abcdefghijklmnop";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sprawl-tests-" + Guid.NewGuid().ToString("N"));

    public TestClock Clock { get; } = new();
    public byte[] TotpSecret { get; } = RandomNumberGenerator.GetBytes(20);
    public string EnvironmentName { get; init; } = "Testing";
    public Dictionary<string, string?> Settings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        var verifier = Path.Combine(directory, "owner.verifier");
        File.WriteAllText(verifier, new PasswordHasher<string>().HashPassword("owner", Password));
        var totp = Path.Combine(directory, "owner.totp");
        File.WriteAllText(totp, Totp.ToBase32(TotpSecret));
        builder.UseEnvironment(EnvironmentName);
        var settings = new Dictionary<string, string?>
        {
            ["Api:DatabasePath"] = Path.Combine(directory, "sprawl.db"),
            ["Api:DevicePort"] = DevicePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Api:PublicOrigin"] = "https://localhost",
            ["Api:SnapshotEvery"] = "10",
            ["Auth:CredentialFile"] = verifier,
            ["Auth:TotpFile"] = totp,
            ["Auth:DeviceTokenSha256:0"] = DeviceTokens.Hash(DeviceToken),
            ["AllowedHosts"] = "*"
        };
        foreach (var (key, value) in Settings) settings[key] = value;
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStartupFilter, TestConnectionFilter>();
        });
    }

    /// <summary>An owner session on the UI listener: signed in with password + TOTP, sending the anti-CSRF header.</summary>
    public async Task<HttpClient> Owner()
    {
        var client = UiClient();
        Clock.Advance(TimeSpan.FromSeconds(31)); // a fresh TOTP step for each sign-in
        var response = await client.PostAsJsonAsync("/api/v1/session", new { password = Password, code = Code() });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    public HttpClient UiClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Sprawl-Request", "1");
        client.DefaultRequestHeaders.Add(TestConnectionFilter.PortHeader, "443");
        return client;
    }

    /// <summary>The MCP's view: the device listener with the device token.</summary>
    public HttpClient Device(string? token = DeviceToken)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestConnectionFilter.PortHeader, DevicePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (token is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Sprawl-Client", "https://claude.ai/oauth/test");
        return client;
    }

    public string Code() => Totp.Code(TotpSecret, Totp.Step(Clock.Now));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public sealed class TestConnectionFilter : IStartupFilter
{
    public const string PortHeader = "X-Test-Local-Port";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (int.TryParse(context.Request.Headers[PortHeader], out var port)) context.Connection.LocalPort = port;
            context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
            await nextMiddleware(context);
        });
        next(app);
    };
}

public static class Api
{
    public static readonly JsonSerializerOptions Json = SprawlJson.Options;

    public static async Task<T> Read<T>(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    public static async Task<JsonElement> Problem(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    public static Task<HttpResponseMessage> Send(this HttpClient client, HttpMethod method, string path, object? body = null, long? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        return client.SendAsync(request);
    }
}
