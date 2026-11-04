using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Sprawl.Api.Auth;
using Sprawl.Api.Configuration;
using Sprawl.Api.Data;
using Sprawl.Api.Endpoints;
using Sprawl.Api.Events;
using Sprawl.Api.Services;
using Sprawl.Contracts;
using Sprawl.Domain;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => { options.SingleLine = true; options.TimestampFormat = "HH:mm:ss "; });

builder.Services.AddOptions<ApiOptions>().BindConfiguration(ApiOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
var apiSettings = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new();
var authSettings = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
ValidateSecurityConfiguration(apiSettings, authSettings, builder.Environment);

builder.Services.ConfigureHttpJsonOptions(options => SprawlJson.Configure(options.SerializerOptions));
// Malformed bodies throw so ProblemMiddleware answers with a problem+json body in every environment.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IIdGenerator>(RandomIdGenerator.Instance);
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<CanvasStore>();
builder.Services.AddSingleton<CanvasWriter>();
builder.Services.AddSingleton<CanvasEventHub>();
builder.Services.AddSingleton<Listeners>();
builder.Services.AddSingleton<DeviceTokens>();
builder.Services.AddSingleton<OwnerCredentials>();
builder.Services.AddSingleton<OwnerSecondFactor>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddHostedService<SampleSeeder>();
builder.Services.AddHostedService<TrashPurger>();

builder.Services.AddAuthentication(SprawlSchemes.Selector)
    .AddPolicyScheme(SprawlSchemes.Selector, null, options =>
        // The listening port decides the scheme: cookies are ignored on the device port, bearer tokens everywhere else.
        options.ForwardDefaultSelector = context => context.RequestServices.GetRequiredService<Listeners>().IsDevice(context) ? SprawlSchemes.Device : SprawlSchemes.Cookie)
    .AddCookie(SprawlSchemes.Cookie, options =>
    {
        options.Cookie.Name = "__Host-sprawl";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(authSettings.SessionHours);
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; },
            OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; },
            OnValidatePrincipal = context =>
            {
                // Sliding sessions still end a fixed number of days after sign-in.
                var authTime = context.Principal?.FindFirst(SprawlClaims.AuthTime)?.Value;
                var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                if (!long.TryParse(authTime, out var seconds) ||
                    DateTimeOffset.FromUnixTimeSeconds(seconds).AddDays(authSettings.SessionAbsoluteDays) < clock.GetUtcNow())
                    context.RejectPrincipal();
                return Task.CompletedTask;
            }
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DeviceTokenHandler>(SprawlSchemes.Device, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(SprawlClaims.ApiPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim(SprawlClaims.Actor, ActorKinds.Owner, ActorKinds.Mcp))
    .AddPolicy(SprawlClaims.OwnerPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim(SprawlClaims.Actor, ActorKinds.Owner));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
    foreach (var proxy in apiSettings.TrustedProxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);

var app = builder.Build();
await app.Services.GetRequiredService<Database>().MigrateAsync();

var requireHttps = !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing");
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers.XFrameOptions = "DENY";
    if (context.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";
    else headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    var listeners = context.RequestServices.GetRequiredService<Listeners>();
    if (requireHttps && !context.Request.IsHttps && !listeners.IsDevice(context) && !context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { status = 403, code = ErrorCodes.Forbidden, detail = "HTTPS is required." });
        return;
    }
    await next(context);
});
app.UseMiddleware<ProblemMiddleware>();
app.UseMiddleware<RequestSafetyMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (Database database, CancellationToken ct) =>
{
    try
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.Command(null, "SELECT count(*) FROM schema_migrations");
        await command.ExecuteScalarAsync(ct);
        return Results.Ok(new { status = "Ready" });
    }
    catch (Exception error) when (error is not OperationCanceledException)
    {
        return Results.Json(new { status = "NotReady" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapSessions(app.Environment);
app.MapCanvases();
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: 404, detail: "No such API route.",
    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound })).AllowAnonymous();

// The built SPA (copied into wwwroot by scripts/build-local.ps1 and the Dockerfile). Absent until M2.
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html")))
{
    // Hashed assets are immutable; the shell must revalidate so a new release is picked up.
    var spaFiles = new StaticFileOptions
    {
        OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl = context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                ? "no-cache" : "public, max-age=31536000, immutable"
    };
    app.UseDefaultFiles();
    app.UseStaticFiles(spaFiles);
    app.MapFallbackToFile("index.html", spaFiles).AllowAnonymous();
}

app.Run();

static void ValidateSecurityConfiguration(ApiOptions api, AuthOptions auth, IHostEnvironment environment)
{
    if (auth.DevAutoSignIn && !environment.IsDevelopment())
        throw new InvalidOperationException("Auth:DevAutoSignIn is allowed only in the Development environment.");
    if (api.SeedSample && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("Api:SeedSample is allowed only in Development.");
    if (environment.IsDevelopment() || environment.IsEnvironment("Testing")) return;
    if (string.IsNullOrWhiteSpace(auth.CredentialFile)) throw new InvalidOperationException("Auth:CredentialFile is required outside Development.");
    if (string.IsNullOrWhiteSpace(auth.TotpFile)) throw new InvalidOperationException("Auth:TotpFile is required outside Development.");
    if (!Uri.TryCreate(api.PublicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps || origin.AbsolutePath != "/")
        throw new InvalidOperationException("Api:PublicOrigin must be an https origin outside Development.");
    foreach (var proxy in api.TrustedProxies)
        if (!IPAddress.TryParse(proxy, out _)) throw new InvalidOperationException("Api:TrustedProxies must be IP addresses.");
}

public partial class Program;
