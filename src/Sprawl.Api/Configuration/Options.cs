using System.ComponentModel.DataAnnotations;

namespace Sprawl.Api.Configuration;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>The browser origin of the UI, e.g. https://sprawl.tailf67d61.ts.net. Unsafe requests must come from it.</summary>
    [Required, Url] public string PublicOrigin { get; set; } = "https://localhost:7543";
    /// <summary>The listening port that accepts only the MCP device token (never cookies). 0 disables device access.</summary>
    [Range(0, 65535)] public int DevicePort { get; set; } = 8081;
    [Required] public string DatabasePath { get; set; } = "";
    /// <summary>Seed the sample canvas into an empty database. Development only.</summary>
    public bool SeedSample { get; set; }
    public string[] TrustedProxies { get; set; } = [];
    [Range(1, 365)] public int TrashDays { get; set; } = 30;
    [Range(10, 100_000)] public int SnapshotEvery { get; set; } = 100;
    public BackupOptions Backup { get; set; } = new();
}

public sealed class BackupOptions
{
    /// <summary>Where nightly verified copies go (e.g. /data/backups). Empty disables backups.</summary>
    public string Directory { get; set; } = "";
    [Range(0, 23)] public int HourUtc { get; set; } = 2;
    [Range(1, 365)] public int Keep { get; set; } = 14;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The owner's stable identifier inside Sprawl (also the verifier's user key).</summary>
    [Required, RegularExpression("^[A-Za-z0-9-]{1,64}$")] public string OwnerId { get; set; } = "owner";
    /// <summary>Identity V3 password verifier file, as produced by orbit-cloud's Orbit.Credentials tool.</summary>
    public string CredentialFile { get; set; } = "";
    /// <summary>Base32 TOTP secret file (Orbit.Credentials --totp). Required outside Development.</summary>
    public string TotpFile { get; set; } = "";
    /// <summary>Development only: one-click loopback sign-in without a password.</summary>
    public bool DevAutoSignIn { get; set; }
    /// <summary>SHA-256 (hex) of accepted MCP device tokens; at most two, for rotation.</summary>
    public string[] DeviceTokenSha256 { get; set; } = [];
    /// <summary>A file holding one SHA-256 hex hash per line, as an alternative to <see cref="DeviceTokenSha256"/>.</summary>
    public string DeviceTokenSha256File { get; set; } = "";
    [Range(1, 72)] public int SessionHours { get; set; } = 12;
    [Range(1, 30)] public int SessionAbsoluteDays { get; set; } = 7;
}
