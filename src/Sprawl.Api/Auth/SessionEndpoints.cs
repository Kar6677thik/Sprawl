using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Sprawl.Api.Configuration;
using Sprawl.Contracts;

namespace Sprawl.Api.Auth;

public static class SessionEndpoints
{
    public sealed record SignInRequest(string Password, string? Code = null);

    public static void MapSessions(this IEndpointRouteBuilder app, IHostEnvironment environment)
    {
        var group = app.MapGroup("/api/v1/session");
        group.MapGet("", (HttpContext context, OwnerCredentials credentials, OwnerSecondFactor secondFactor, IOptions<AuthOptions> options) => Results.Ok(new
        {
            signedIn = context.User.FindFirstValue(SprawlClaims.Actor) == ActorKinds.Owner,
            passwordSignIn = credentials.Available,
            secondFactor = secondFactor.Required ? "totp" : null,
            devSignIn = DevSignInAllowed(options.Value, environment)
        })).AllowAnonymous();

        group.MapPost("", async (SignInRequest request, HttpContext context, OwnerCredentials credentials, OwnerSecondFactor secondFactor,
            LoginThrottle throttle, IOptions<AuthOptions> options, TimeProvider clock) =>
        {
            if (!credentials.Available) return Problem(503, "Password sign-in is not configured.");
            if (throttle.Locked) return Problem(429, "Too many failed sign-ins; wait 15 minutes.");
            if (string.IsNullOrEmpty(request.Password) || Encoding.UTF8.GetByteCount(request.Password) > 1024) { throttle.Fail(); return Problem(401, "Sign-in failed."); }
            if (!await credentials.HashSlots.WaitAsync(0, context.RequestAborted)) return Problem(429, "Please wait before signing in again.");
            bool accepted;
            // A code is consumed only after the password matches, so wrong-password guesses cannot burn the current code.
            try { accepted = credentials.Verify(options.Value.OwnerId, request.Password) && secondFactor.Verify(request.Code, clock.GetUtcNow()); }
            finally { credentials.HashSlots.Release(); }
            if (!accepted) { throttle.Fail(); return Problem(401, "Sign-in failed."); }
            throttle.Succeed();
            await SignIn(context, options.Value, clock);
            return Results.NoContent();
        }).AllowAnonymous();

        group.MapDelete("", async (HttpContext context) =>
        {
            await context.SignOutAsync(SprawlSchemes.Cookie);
            return Results.NoContent();
        }).RequireAuthorization(SprawlClaims.OwnerPolicy);

        if (environment.IsDevelopment())
            group.MapPost("/dev", async (HttpContext context, IOptions<AuthOptions> options, TimeProvider clock) =>
            {
                if (!DevSignInAllowed(options.Value, environment)) return Results.NotFound();
                if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
                    return Problem(403, "Development sign-in only works from this machine.");
                await SignIn(context, options.Value, clock);
                return Results.NoContent();
            }).AllowAnonymous();
    }

    public static bool DevSignInAllowed(AuthOptions options, IHostEnvironment environment) => options.DevAutoSignIn && environment.IsDevelopment();

    private static async Task SignIn(HttpContext context, AuthOptions options, TimeProvider clock)
    {
        var identity = new ClaimsIdentity([
            new Claim(SprawlClaims.Subject, options.OwnerId),
            new Claim(SprawlClaims.Actor, ActorKinds.Owner),
            new Claim(SprawlClaims.AuthTime, clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))
        ], SprawlSchemes.Cookie, SprawlClaims.Subject, null);
        await context.SignInAsync(SprawlSchemes.Cookie, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    private static IResult Problem(int status, string detail) =>
        Results.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = status == 401 ? ErrorCodes.Unauthorized : ErrorCodes.Forbidden });
}

/// <summary>
/// Cross-site request protection for the cookie listener: unsafe API methods need the custom <c>X-Sprawl-Request</c>
/// header (which forces a CORS preflight no other origin can pass) and, when present, an Origin equal to the UI origin.
/// </summary>
public sealed class RequestSafetyMiddleware(RequestDelegate next, IOptions<ApiOptions> options, Listeners listeners)
{
    private readonly string origin = new Uri(options.Value.PublicOrigin).GetLeftPart(UriPartial.Authority);

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!listeners.IsDevice(context) && request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method))
        {
            var originHeader = request.Headers.Origin.ToString();
            var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
            if (request.Headers["X-Sprawl-Request"] != "1" ||
                (originHeader.Length > 0 && !string.Equals(originHeader, origin, StringComparison.OrdinalIgnoreCase)) ||
                fetchSite is "cross-site" or "same-site")
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { status = 403, code = ErrorCodes.Forbidden, detail = "Cross-site request refused." }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
                return;
            }
        }
        await next(context);
    }
}
