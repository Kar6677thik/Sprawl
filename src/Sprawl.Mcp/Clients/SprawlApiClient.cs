using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprawl.Contracts;
using Sprawl.Mcp.Configuration;

namespace Sprawl.Mcp.Clients;

/// <summary>A refusal from the Sprawl API, carrying its stable problem code.</summary>
public sealed class SprawlUpstreamException(int status, string code, string message, long? currentVersion = null, IReadOnlyList<string>? entities = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public long? CurrentVersion { get; } = currentVersion;
    public IReadOnlyList<string> Entities { get; } = entities ?? [];
    public bool Retryable => Status is 429 or >= 500;
}

public sealed class UpstreamResponseTooLargeException : Exception;

/// <summary>
/// Calls the Sprawl API's device listener with the MCP's own device token. The caller's OAuth token is never forwarded;
/// the OAuth client id is passed only as a history label (<c>X-Sprawl-Client</c>).
/// </summary>
public sealed class SprawlApiClient(HttpClient http, IOptions<SprawlMcpOptions> options, IConfiguration configuration, IHttpContextAccessor context)
{
    public static readonly JsonSerializerOptions Json = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private readonly SprawlMcpOptions settings = options.Value;
    private readonly string token = DeviceToken(configuration);

    public static JsonSerializerOptions Configure(JsonSerializerOptions json)
    {
        json.AllowOutOfOrderMetadataProperties = true;
        return json;
    }

    public Task<List<CanvasSummary>> ListCanvases(string? tag, string? collection, string? query, bool includeArchived, int limit, CancellationToken ct) =>
        Get<List<CanvasSummary>>($"api/v1/canvases?{Query(("tag", tag), ("collection", collection), ("q", query), ("archived", includeArchived ? "true" : null), ("limit", Num(limit)))}", ct);

    public Task<SearchResult> Search(string query, string? canvasId, IReadOnlyList<string> kinds, int limit, CancellationToken ct) =>
        Get<SearchResult>($"api/v1/search?{Query(("q", query), ("canvas", canvasId), ("kinds", kinds.Count == 0 ? null : string.Join(',', kinds)), ("limit", Num(limit)))}", ct);

    public async Task<(string Outline, long Version)> Outline(string canvasId, string depth, string? region, bool connections, int maxCards, CancellationToken ct)
    {
        using var response = await Send(HttpMethod.Get, $"api/v1/canvases/{Id(canvasId)}/outline?{Query(("depth", depth), ("region", region), ("connections", connections ? "true" : "false"), ("maxCards", Num(maxCards)))}", null, null, ct);
        var text = await Read(response, ct);
        return (text, VersionOf(response));
    }

    public Task<List<CardDetail>> Cards(IReadOnlyList<string> ids, CancellationToken ct) => Call<List<CardDetail>>(HttpMethod.Post, "api/v1/cards/query", new CardsQuery(ids), null, ct);

    public async Task<string> Mermaid(string canvasId, string region, CancellationToken ct)
    {
        using var response = await Send(HttpMethod.Get, $"api/v1/canvases/{Id(canvasId)}/regions/{Uri.EscapeDataString(region)}/mermaid", null, null, ct);
        return await Read(response, ct);
    }

    public Task<ReviewQueue> Reviews(string? canvasId, int limit, CancellationToken ct) =>
        Get<ReviewQueue>($"api/v1/reviews?{Query(("canvas", canvasId), ("limit", Num(limit)))}", ct);

    public Task<CanvasInsights> Insights(string canvasId, CancellationToken ct) => Get<CanvasInsights>($"api/v1/canvases/{Id(canvasId)}/insights", ct);

    public Task<CardContext> Context(string canvasId, string cardId, CancellationToken ct) =>
        Get<CardContext>($"api/v1/canvases/{Id(canvasId)}/cards/{Id(cardId)}/context", ct);

    public Task<List<HistoryEntry>> History(string canvasId, long? before, int limit, CancellationToken ct) =>
        Get<List<HistoryEntry>>($"api/v1/canvases/{Id(canvasId)}/history?{Query(("before", before?.ToString(CultureInfo.InvariantCulture)), ("limit", Num(limit)))}", ct);

    public Task<BatchResult> Create(CreateCanvasRequest request, CancellationToken ct) => Call<BatchResult>(HttpMethod.Post, "api/v1/canvases", request, null, ct);

    public Task<BatchResult> Batch(string canvasId, BatchRequest request, long? expectedVersion, CancellationToken ct) =>
        Call<BatchResult>(HttpMethod.Post, $"api/v1/canvases/{Id(canvasId)}/batches", request, expectedVersion, ct);

    public Task<BatchResult> UpdateCanvas(string canvasId, UpdateCanvasIntent update, long? expectedVersion, CancellationToken ct) =>
        Call<BatchResult>(HttpMethod.Patch, $"api/v1/canvases/{Id(canvasId)}", update, expectedVersion, ct);

    public Task<BatchResult> Revert(string canvasId, long toVersion, long expectedVersion, CancellationToken ct) =>
        Call<BatchResult>(HttpMethod.Post, $"api/v1/canvases/{Id(canvasId)}/revert", new RevertRequest(toVersion), expectedVersion, ct);

    public Task<BatchResult> Trash(string canvasId, long expectedVersion, CancellationToken ct) =>
        Call<BatchResult>(HttpMethod.Delete, $"api/v1/canvases/{Id(canvasId)}", null, expectedVersion, ct);

    public async Task<bool> Ready(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "health/ready");
        using var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    private Task<T> Get<T>(string path, CancellationToken ct) => Call<T>(HttpMethod.Get, path, null, null, ct);

    private async Task<T> Call<T>(HttpMethod method, string path, object? body, long? ifMatch, CancellationToken ct)
    {
        using var response = await Send(method, path, body, ifMatch, ct);
        var text = await Read(response, ct);
        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new SprawlUpstreamException(502, "upstream_invalid", "The Sprawl API returned an empty response.");
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, long? ifMatch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new SprawlUpstreamException(503, "api_auth_missing", "The MCP server has no Sprawl API device token configured.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        if (ClientLabel() is { } client) request.Headers.TryAddWithoutValidation("X-Sprawl-Client", client);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch.Value.ToString(CultureInfo.InvariantCulture)}\"");
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode) return response;
        using (response)
        {
            var text = await Read(response, ct);
            throw Problem((int)response.StatusCode, text);
        }
    }

    private async Task<string> Read(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[settings.MaximumResponseBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) break;
            total += read;
        }
        if (total > settings.MaximumResponseBytes) throw new UpstreamResponseTooLargeException();
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static SprawlUpstreamException Problem(int status, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() ?? $"http_{status}" : $"http_{status}";
            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
            long? version = root.TryGetProperty("currentVersion", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
            var entities = root.TryGetProperty("entities", out var e) && e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
            return new SprawlUpstreamException(status, code, detail.Length > 0 ? detail : $"The Sprawl API refused the request ({status}).", version, entities);
        }
        catch (JsonException)
        {
            return new SprawlUpstreamException(status, status == 401 ? "api_auth_rejected" : $"http_{status}",
                status == 401 ? "The Sprawl API rejected the MCP device token." : $"The Sprawl API refused the request ({status}).");
        }
    }

    private static long VersionOf(HttpResponseMessage response) =>
        long.TryParse(response.Headers.ETag?.Tag.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : 0;

    /// <summary>The OAuth client id of the caller (e.g. claude.ai's metadata URL), for the history view. A label, never authority.</summary>
    private string? ClientLabel()
    {
        var user = context.HttpContext?.User;
        var client = user?.FindFirstValue("client_id") ?? user?.FindFirstValue("azp");
        return client is { Length: > 0 and <= 200 } && !client.Any(char.IsControl) ? client : null;
    }

    private static string DeviceToken(IConfiguration configuration)
    {
        var file = configuration["Secrets:ApiDeviceTokenFile"];
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file)) return File.ReadAllText(file).Trim();
        return configuration["Secrets:ApiDeviceToken"]?.Trim() ?? "";
    }

    private static string Id(string id) => Uri.EscapeDataString(id);
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Query(params (string Name, string? Value)[] parts) =>
        string.Join('&', parts.Where(part => !string.IsNullOrEmpty(part.Value)).Select(part => $"{part.Name}={Uri.EscapeDataString(part.Value!)}"));
}
