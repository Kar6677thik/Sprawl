using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Sprawl.Contracts;

namespace Sprawl.Api.Tests;

public sealed class AuthTests(SprawlFactory factory) : IClassFixture<SprawlFactory>
{
    [Fact]
    public async Task Anonymous_requests_are_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.UiClient().GetAsync("/api/v1/canvases")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Device(token: null).GetAsync("/api/v1/canvases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.UiClient().GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Sign_in_needs_the_password_and_a_fresh_totp_code()
    {
        var client = factory.UiClient();
        factory.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/session", new { password = "wrong", code = factory.Code() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/session", new { password = SprawlFactory.Password, code = "000000" == factory.Code() ? "111111" : "000000" })).StatusCode);
        var code = factory.Code();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/session", new { password = SprawlFactory.Password, code })).StatusCode);
        var session = await (await client.GetAsync("/api/v1/session")).Read<JsonElement>();
        Assert.True(session.GetProperty("signedIn").GetBoolean());
        Assert.Equal("totp", session.GetProperty("secondFactor").GetString());

        // The same code cannot be replayed by someone who observed it.
        var replay = factory.UiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.PostAsJsonAsync("/api/v1/session", new { password = SprawlFactory.Password, code })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/canvases")).StatusCode);
    }

    [Fact]
    public async Task Sessions_end_after_the_absolute_lifetime_even_when_sliding()
    {
        using var isolated = new SprawlFactory();
        var owner = await isolated.Owner();
        // Use the session every 10 hours, so the 12-hour sliding window keeps renewing it...
        for (var hours = 10; hours <= 160; hours += 10)
        {
            isolated.Clock.Advance(TimeSpan.FromHours(10));
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/canvases")).StatusCode);
        }
        // ...until seven days after sign-in, when it ends regardless.
        isolated.Clock.Advance(TimeSpan.FromHours(10));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/v1/canvases")).StatusCode);
    }

    [Fact]
    public async Task Unsafe_cookie_requests_need_the_custom_header_and_the_ui_origin()
    {
        var owner = await factory.Owner();
        using var noHeader = new HttpRequestMessage(HttpMethod.Post, "/api/v1/canvases") { Content = JsonContent.Create(new CreateCanvasRequest("x"), options: Api.Json) };
        noHeader.Headers.Remove("X-Sprawl-Request");
        var bare = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        bare.DefaultRequestHeaders.Add(TestConnectionFilter.PortHeader, "443");
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.SendAsync(noHeader)).StatusCode);

        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/v1/canvases") { Content = JsonContent.Create(new CreateCanvasRequest("x"), options: Api.Json) };
        foreign.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.SendAsync(foreign)).StatusCode);

        using var same = new HttpRequestMessage(HttpMethod.Post, "/api/v1/canvases") { Content = JsonContent.Create(new CreateCanvasRequest("Same origin"), options: Api.Json) };
        same.Headers.Add("Origin", "https://localhost");
        Assert.Equal(HttpStatusCode.Created, (await owner.SendAsync(same)).StatusCode);
    }

    [Fact]
    public async Task Each_listener_accepts_only_its_own_credential()
    {
        // The device token is useless on the UI listener...
        var tokenOnUi = factory.UiClient();
        tokenOnUi.DefaultRequestHeaders.Authorization = new("Bearer", SprawlFactory.DeviceToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tokenOnUi.GetAsync("/api/v1/canvases")).StatusCode);

        // ...and an owner cookie is useless on the device listener.
        var owner = await factory.Owner();
        using var cookieOnDevice = new HttpRequestMessage(HttpMethod.Get, "/api/v1/canvases");
        cookieOnDevice.Headers.Add(TestConnectionFilter.PortHeader, SprawlFactory.DevicePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        owner.DefaultRequestHeaders.Remove(TestConnectionFilter.PortHeader);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.SendAsync(cookieOnDevice)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Device("spr_dev_wrong").GetAsync("/api/v1/canvases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Device().GetAsync("/api/v1/canvases")).StatusCode);
    }

    [Fact]
    public async Task Repeated_failures_lock_sign_in()
    {
        using var isolated = new SprawlFactory();
        var client = isolated.UiClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/session", new { password = "guess" + i })).StatusCode);
        isolated.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/session", new { password = SprawlFactory.Password, code = isolated.Code() })).StatusCode);
        isolated.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/session", new { password = SprawlFactory.Password, code = isolated.Code() })).StatusCode);
    }

    [Fact]
    public void Development_sign_in_is_refused_outside_development()
    {
        using var unsafeFactory = new SprawlFactory { EnvironmentName = "Production" };
        unsafeFactory.Settings["Auth:DevAutoSignIn"] = "true";
        var error = Assert.ThrowsAny<Exception>(() => unsafeFactory.CreateClient());
        Assert.Contains("DevAutoSignIn is allowed only in the Development environment", Flatten(error));
    }

    [Fact]
    public void Production_requires_a_password_verifier_and_totp()
    {
        using var noTotp = new SprawlFactory { EnvironmentName = "Production" };
        noTotp.Settings["Auth:TotpFile"] = "";
        Assert.Contains("Auth:TotpFile is required", Flatten(Assert.ThrowsAny<Exception>(() => noTotp.CreateClient())));
    }

    [Fact]
    public async Task Development_sign_in_works_from_loopback_when_enabled()
    {
        using var development = new SprawlFactory { EnvironmentName = "Development" };
        development.Settings["Auth:DevAutoSignIn"] = "true";
        var client = development.UiClient();
        var session = await (await client.GetAsync("/api/v1/session")).Read<JsonElement>();
        Assert.True(session.GetProperty("devSignIn").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/session/dev", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/canvases")).StatusCode);
    }

    [Fact]
    public async Task Production_refuses_plain_http_on_the_ui_listener_but_not_on_the_device_listener()
    {
        using var production = new SprawlFactory { EnvironmentName = "Production" };
        var plain = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });
        plain.DefaultRequestHeaders.Add(TestConnectionFilter.PortHeader, "8080");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync("/api/v1/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await production.Device().GetAsync("/api/v1/canvases")).StatusCode);
    }

    private static string Flatten(Exception error)
    {
        var messages = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException) messages.Add(current.Message);
        if (error is AggregateException aggregate) messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
        return string.Join(" | ", messages);
    }
}
