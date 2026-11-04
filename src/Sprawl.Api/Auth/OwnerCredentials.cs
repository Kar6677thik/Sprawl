// Adapted from orbit-cloud src/Orbit.Api/Authentication/OwnerCredentials.cs at 72b6d95: the same Identity V3 verifier
// file and TOTP secret file formats, so Orbit.Credentials provisions both apps. Sprawl uses its own files.
using System.Buffers.Binary;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sprawl.Api.Configuration;

namespace Sprawl.Api.Auth;

public sealed class OwnerCredentials
{
    private readonly string? verifier;
    private readonly PasswordHasher<string> hasher = new();
    /// <summary>Bounds concurrent password hashing (CPU-expensive).</summary>
    public SemaphoreSlim HashSlots { get; } = new(2, 2);

    public OwnerCredentials(IOptions<AuthOptions> options)
    {
        var file = options.Value.CredentialFile;
        if (string.IsNullOrWhiteSpace(file)) return; // Development without password sign-in; production validation requires it.
        verifier = ReadBounded(file, 20, 1024, "Owner verifier file is missing or invalid.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(verifier); }
        catch (FormatException) { throw new InvalidOperationException("Owner verifier format is invalid."); }
        if (bytes.Length < 49 || bytes[0] != 1 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4)) > 2 ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4)) is < 100_000 or > 1_000_000 ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9, 4)) is < 16 or > 128 ||
            bytes.Length < 13 + BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9, 4)) + 32)
            throw new InvalidOperationException("Owner verifier must use the framework Identity V3 format and a bounded work factor.");
    }

    public bool Available => verifier is not null;

    public bool Verify(string ownerId, string password) =>
        verifier is not null && hasher.VerifyHashedPassword(ownerId, verifier, password) != PasswordVerificationResult.Failed;

    /// <summary>
    /// Kubernetes Secret keys are symlinks. Inspect and bound the opened target, not FileInfo.Length
    /// (which can report the symlink's own byte length).
    /// </summary>
    internal static string ReadBounded(string path, int min, int max, string error)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[max + 1];
            var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (count < min || count > max) throw new InvalidOperationException(error);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, count).Trim();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { throw new InvalidOperationException(error); }
    }
}

/// <summary>
/// Optional TOTP second factor; required outside Development by startup validation. Each accepted step is single-use,
/// so an observed code cannot be replayed.
/// </summary>
public sealed class OwnerSecondFactor
{
    private readonly byte[]? secret;
    private long lastAcceptedStep = long.MinValue;
    private readonly Lock gate = new();

    public OwnerSecondFactor(IOptions<AuthOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.TotpFile)) return;
        try { secret = Totp.FromBase32(OwnerCredentials.ReadBounded(options.Value.TotpFile, 16, 128, "Owner TOTP file is missing or invalid.")); }
        catch (FormatException) { throw new InvalidOperationException("Owner TOTP file is missing or invalid."); }
        if (secret.Length < Totp.SecretBytes) throw new InvalidOperationException("Owner TOTP secret must be at least 160 bits.");
    }

    public bool Required => secret is not null;

    public bool Verify(string? code, DateTimeOffset now)
    {
        if (secret is null) return true;
        if (code is null || Totp.Match(secret, code, now) is not { } step) return false;
        lock (gate)
        {
            if (step <= lastAcceptedStep) return false;
            lastAcceptedStep = step;
            return true;
        }
    }
}

/// <summary>Five failed sign-ins within 15 minutes lock sign-in for 15 minutes from the first of them.</summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaxFailures = 5;
    private readonly Queue<DateTimeOffset> failures = new();
    private readonly Lock gate = new();

    public bool Locked
    {
        get
        {
            lock (gate)
            {
                Trim();
                return failures.Count >= MaxFailures;
            }
        }
    }

    public void Fail()
    {
        lock (gate) { Trim(); failures.Enqueue(clock.GetUtcNow()); }
    }

    public void Succeed()
    {
        lock (gate) failures.Clear();
    }

    private void Trim()
    {
        var cutoff = clock.GetUtcNow() - Window;
        while (failures.Count > 0 && failures.Peek() < cutoff) failures.Dequeue();
    }
}
