using System.ComponentModel.DataAnnotations;

namespace Sprawl.Mcp.Configuration;

public sealed class SprawlMcpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>The public origin of this server, e.g. https://canvas-mcp.karthiksurkanti.in.</summary>
    [Required, Url] public string PublicOrigin { get; set; } = "http://localhost:5181";
    /// <summary>The Sprawl API's device listener, e.g. http://10.43.0.101:8081. Never the UI listener.</summary>
    [Required, Url] public string ApiBaseUrl { get; set; } = "http://127.0.0.1:7544";
    /// <summary>Hosts ApiBaseUrl may point at. HTTP is allowed only for these (in-cluster or loopback).</summary>
    public string[] AllowedApiHosts { get; set; } = ["127.0.0.1", "localhost"];
    /// <summary>Browser origins allowed to call /mcp (requests without an Origin header are not affected).</summary>
    public string[] AllowedOrigins { get; set; } = ["https://claude.ai"];
    /// <summary>
    /// Optional: the Sprawl web app's origin, e.g. https://sprawl.example.ts.net. When set, results carry <c>url</c> (a canvas) and
    /// <c>webUrl</c> (a card or region) that open it in the app. Absolute HTTPS origin only; checked at startup.
    /// </summary>
    public string? WebAppUrl { get; set; }
    [Range(1, 120)] public int RequestTimeoutSeconds { get; set; } = 25;
    [Range(1024, 8 * 1024 * 1024)] public int MaximumResponseBytes { get; set; } = 2 * 1024 * 1024;
}

public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    /// <summary>Orbit Identity's exact issuer, e.g. https://auth.karthiksurkanti.in/. Empty only in Development (dev bypass).</summary>
    public string Authority { get; set; } = "";
    [Required] public string Audience { get; set; } = "http://localhost:5181/mcp";
    [Required, Url] public string Resource { get; set; } = "http://localhost:5181/mcp";
    public string[] AuthorizationServers { get; set; } = [];
    /// <summary>The owner's subject. Single user: every other subject is refused.</summary>
    public string[] AllowedSubjectIds { get; set; } = [];
    /// <summary>Optional allowlist of OAuth client ids (azp / client_id). Empty means any client Orbit Identity approved.</summary>
    public string[] AllowedCallerIds { get; set; } = [];
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";
    [Range(1, 1000)] public int PermitLimit { get; set; } = 90;
    [Range(1, 3600)] public int WindowSeconds { get; set; } = 60;
    [Range(0, 100)] public int QueueLimit { get; set; } = 4;
}
