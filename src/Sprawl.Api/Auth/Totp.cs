// Adapted from orbit-cloud src/Orbit.Api/Authentication/Totp.cs at 72b6d95, so Sprawl accepts the same TOTP enrollment
// format as Orbit (Orbit.Credentials --totp). Keep the two in step.
using System.Security.Cryptography;
using System.Text;

namespace Sprawl.Api.Auth;

/// <summary>
/// RFC 6238 time-based one-time codes with the parameters every authenticator app supports:
/// HMAC-SHA1, 30-second steps, six digits. Shared with the credential tool for enrollment.
/// </summary>
public static class Totp
{
    public const int SecretBytes = 20;
    public const int StepSeconds = 30;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static long Step(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(ReadOnlySpan<byte> secret, long step, int digits = 6)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % (int)Math.Pow(10, digits)).ToString(new string('0', digits));
    }

    /// <summary>Returns the matching step within one step of clock skew, or null. Comparison is constant-time.</summary>
    public static long? Match(ReadOnlySpan<byte> secret, string code, DateTimeOffset now)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) return null;
        long? matched = null;
        var current = Step(now);
        for (var step = current - 1; step <= current + 1; step++)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(code)))
                matched ??= step;
        return matched;
    }

    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { output.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    public static byte[] FromBase32(string text)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text.TrimEnd('='))
        {
            var index = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (index < 0) throw new FormatException("Invalid base32 character.");
            buffer = (buffer << 5) | index; bits += 5;
            if (bits >= 8) { output.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return [.. output];
    }
}
