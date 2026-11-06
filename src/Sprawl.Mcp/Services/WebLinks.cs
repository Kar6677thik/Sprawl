using Microsoft.Extensions.Options;
using Sprawl.Mcp.Configuration;

namespace Sprawl.Mcp.Services;

/// <summary>
/// Addresses a person can open in the Sprawl web app: <c>{Mcp:WebAppUrl}/c/{canvas}</c>, and <c>?focus={card or region}</c> to
/// open on one item. Without Mcp:WebAppUrl there are none, and results leave their <c>url</c>/<c>webUrl</c> out. Clients copy these
/// as they are; they never build them. Ids are checked to be plain ids before they go into a URL.
/// </summary>
public sealed class WebLinks(IOptions<SprawlMcpOptions> options)
{
    private readonly string? origin = string.IsNullOrWhiteSpace(options.Value.WebAppUrl) ? null : options.Value.WebAppUrl.Trim().TrimEnd('/');

    public string? Canvas(string? canvasId) => origin is null || !IsId(canvasId) ? null : $"{origin}/c/{canvasId}";

    public string? Item(string? canvasId, string? itemId) =>
        Canvas(canvasId) is { } canvas && IsId(itemId) && (itemId!.StartsWith("cd_", StringComparison.Ordinal) || itemId.StartsWith("rg_", StringComparison.Ordinal))
            ? $"{canvas}?focus={itemId}" : null;

    private static bool IsId(string? id) => id is { Length: > 3 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
