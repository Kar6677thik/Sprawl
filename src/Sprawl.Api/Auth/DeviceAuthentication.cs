using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Sprawl.Api.Configuration;
using Sprawl.Contracts;

namespace Sprawl.Api.Auth;

public static class SprawlClaims
{
    public const string Actor = "actor";
    public const string Subject = "sub";
    public const string AuthTime = "auth_time";
    public const string OwnerPolicy = "owner";
    public const string ApiPolicy = "api";
}

public static class SprawlSchemes
{
    public const string Selector = "Sprawl";
    public const string Cookie = "SprawlCookie";
    public const string Device = "SprawlDevice";
}

/// <summary>
/// Which listener a request arrived on. The device port accepts only the MCP device token; every other port accepts only
/// the owner cookie. Decided by the local port the connection was accepted on, never by a client-supplied header.
/// </summary>
public sealed class Listeners(IOptions<ApiOptions> options)
{
    public bool IsDevice(HttpContext context) => options.Value.DevicePort > 0 && context.Connection.LocalPort == options.Value.DevicePort;
}

/// <summary>SHA-256 hashes of accepted device tokens. Only the hash is ever stored or configured.</summary>
public sealed class DeviceTokens
{
    private readonly List<byte[]> hashes = [];

    public DeviceTokens(IOptions<AuthOptions> options)
    {
        var values = options.Value.DeviceTokenSha256.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(options.Value.DeviceTokenSha256File))
            values = values.Concat(OwnerCredentials.ReadBounded(options.Value.DeviceTokenSha256File, 64, 200, "Device token hash file is missing or invalid.")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidOperationException("Device token hashes must be 64 hex characters (SHA-256).");
            hashes.Add(Convert.FromHexString(value));
        }
        if (hashes.Count > 2) throw new InvalidOperationException("Configure at most two device token hashes (current and next).");
    }

    public bool Configured => hashes.Count > 0;

    public bool Accepts(string token)
    {
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var accepted = false;
        foreach (var hash in hashes) accepted |= CryptographicOperations.FixedTimeEquals(presented, hash);
        return accepted;
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class DeviceTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, DeviceTokens tokens, Listeners listeners)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!listeners.IsDevice(Context)) return Task.FromResult(AuthenticateResult.NoResult());
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
        var token = header[7..].Trim();
        if (token.Length is 0 or > 256 || !tokens.Accepts(token)) return Task.FromResult(AuthenticateResult.Fail("The device token is not accepted."));
        var identity = new ClaimsIdentity([new Claim(SprawlClaims.Subject, "mcp"), new Claim(SprawlClaims.Actor, ActorKinds.Mcp)], Scheme.Name, SprawlClaims.Subject, null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
