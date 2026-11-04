using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Sprawl.Mcp.Configuration;

namespace Sprawl.Mcp.Security;

public static class CanvasScopes
{
    public const string Read = "canvas.read";
    public const string Write = "canvas.write";
    public const string Organize = "canvas.organize";
    /// <summary>Delete cards and canvases. Granted only when ticked at consent; implies nothing else.</summary>
    public const string Admin = "canvas.admin";
    public static readonly string[] All = [Read, Write, Organize, Admin];
    /// <summary>What a 401 asks for: everyday use. Admin is never requested by default.</summary>
    public const string Challenge = Read + " " + Write + " " + Organize;

    public static string Policy(string scope) => "scope:" + scope;

    public static bool Has(ClaimsPrincipal principal, string required) => principal.Claims
        .Where(claim => claim.Type is "scope" or "scp")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Contains(required, StringComparer.Ordinal);
}

/// <summary>OpenAI-compatible OAuth declarations mirrored into each tool's <c>_meta.securitySchemes</c>. Enforcement is the policies.</summary>
internal static class ToolSecurity
{
    public const string Read = """[{"type":"oauth2","scopes":["canvas.read"]}]""";
    public const string Write = """[{"type":"oauth2","scopes":["canvas.write"]}]""";
    public const string Organize = """[{"type":"oauth2","scopes":["canvas.organize"]}]""";
    public const string Admin = """[{"type":"oauth2","scopes":["canvas.admin"]}]""";
}

public static class SubjectAccessPolicy
{
    public static void ValidateProductionConfiguration(OAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Authority)) throw new InvalidOperationException("OAuth:Authority is required outside Development.");
        if (options.AllowedSubjectIds.Length == 0 || options.AllowedSubjectIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("OAuth:AllowedSubjectIds must contain the owner's subject outside Development.");
        if (!string.Equals(options.Audience, options.Resource, StringComparison.Ordinal))
            throw new InvalidOperationException("OAuth:Audience must exactly match OAuth:Resource.");
        if (options.AuthorizationServers.Length != 1 || !string.Equals(options.AuthorizationServers[0], options.Authority, StringComparison.Ordinal))
            throw new InvalidOperationException("OAuth:AuthorizationServers must contain exactly the issuer configured in OAuth:Authority.");
    }

    public static bool IsAllowed(ClaimsPrincipal? principal, IReadOnlyCollection<string> allowedSubjectIds)
    {
        if (principal is null || allowedSubjectIds.Count == 0) return false;
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject) || subject.EndsWith("@clients", StringComparison.OrdinalIgnoreCase)) return false;
        var grant = principal.FindFirstValue("gty") ?? principal.FindFirstValue("grant_type");
        if (grant is "client-credentials" or "client_credentials") return false;
        return allowedSubjectIds.Contains(subject, StringComparer.Ordinal);
    }
}

/// <summary>
/// Development only: accepts one random bearer (from a file under .local) instead of OAuth, with every scope. Program
/// registers it only in Development/Testing with no OAuth:Authority, and refuses to start elsewhere without an Authority.
/// </summary>
public sealed class DevelopmentBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = ExpectedToken(configuration);
        var authorization = Request.Headers.Authorization.ToString();
        // No credentials at all (health checks, metadata): nothing to authenticate, as JwtBearer does.
        if (authorization.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        if (string.IsNullOrWhiteSpace(expected) || !authorization.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(authorization[7..].Trim())))
            return Task.FromResult(AuthenticateResult.Fail("A valid development bearer token is required."));
        var identity = new ClaimsIdentity([
            new Claim("sub", "local-development"),
            new Claim("client_id", "local-development"),
            new Claim("scope", string.Join(' ', CanvasScopes.All))
        ], Scheme.Name, "sub", null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var origin = configuration["Mcp:PublicOrigin"]?.TrimEnd('/') ?? "http://localhost:5181";
        Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{origin}/.well-known/oauth-protected-resource\", scope=\"{CanvasScopes.Challenge}\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private static string? ExpectedToken(IConfiguration configuration)
    {
        var file = configuration["Development:BearerTokenFile"];
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file)) return File.ReadAllText(file).Trim();
        return configuration["Development:BearerToken"];
    }
}

/// <summary>Refuses /mcp requests from browser origins that are not allowed (DNS-rebinding and cross-site protection).</summary>
public sealed class OriginValidationMiddleware(RequestDelegate next, IOptions<SprawlMcpOptions> options)
{
    private readonly HashSet<string> allowed = options.Value.AllowedOrigins.Append(options.Value.PublicOrigin)
        .Select(origin => new Uri(origin).GetLeftPart(UriPartial.Authority)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/mcp") && context.Request.Headers.Origin is { Count: > 0 } origins &&
            (!Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) || origin.UserInfo.Length > 0 || !allowed.Contains(origin.GetLeftPart(UriPartial.Authority))))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "The request origin is not allowed." });
            return;
        }
        await next(context);
    }
}
