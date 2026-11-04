using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Endpoints;

/// <summary>Maps refusals to RFC 9457 problem+json with a stable <c>code</c>. Never echoes request bodies or secrets.</summary>
public sealed class ProblemMiddleware(RequestDelegate next, ILogger<ProblemMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (SprawlException error) when (!context.Response.HasStarted)
        {
            await Write(context, error.Status, error.Code, error.Message, error.CurrentVersion, error.Entities);
        }
        catch (BadHttpRequestException error) when (!context.Response.HasStarted)
        {
            var detail = error.StatusCode == StatusCodes.Status413PayloadTooLarge ? "The request body is too large." : "The request is malformed for this endpoint: " + Describe(error);
            await Write(context, error.StatusCode, ErrorCodes.InvalidRequest, detail, null, []);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            logger.LogError(error, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
            await Write(context, 500, "internal_error", "Something went wrong.", null, []);
        }
    }

    // JSON errors name the property path and expectation (no values), which helps a caller fix the request.
    private static string Describe(BadHttpRequestException error) => error.InnerException is System.Text.Json.JsonException json ? json.Message.Split(" Path:")[0] + (json.Path is null ? "" : $" (at {json.Path})") : error.Message;

    public static async Task Write(HttpContext context, int status, string code, string detail, long? currentVersion, IReadOnlyList<string> entities)
    {
        context.Response.StatusCode = status;
        if (currentVersion is { } version) context.Response.Headers.ETag = Http.ETag(version);
        var body = new Dictionary<string, object?>
        {
            ["type"] = "urn:sprawl:error:" + code,
            ["title"] = code.Replace('_', ' '),
            ["status"] = status,
            ["code"] = code,
            ["detail"] = detail
        };
        if (currentVersion is not null) body["currentVersion"] = currentVersion;
        if (entities.Count > 0) body["entities"] = entities;
        await context.Response.WriteAsJsonAsync(body, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}

public static class Http
{
    public static string ETag(long version) => "\"" + version.ToString(CultureInfo.InvariantCulture) + "\"";

    /// <summary>Parse <c>If-Match: "42"</c>. Absent or <c>*</c> means no precondition.</summary>
    public static long? IfMatch(HttpRequest request, bool required = false)
    {
        var header = request.Headers.IfMatch.ToString().Trim();
        if (header.Length == 0 || header == "*")
            return required ? throw new SprawlException(ErrorCodes.PreconditionRequired, "This change needs If-Match with the canvas version you read.", 428) : null;
        var value = header.StartsWith("W/", StringComparison.Ordinal) ? header[2..] : header;
        return value.Length > 2 && value[0] == '"' && value[^1] == '"' && long.TryParse(value[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : throw SprawlException.Invalid("If-Match must be a quoted canvas version, like \"42\".");
    }

    public static Ok<T> WithVersion<T>(HttpContext context, long version, T value)
    {
        context.Response.Headers.ETag = ETag(version);
        return TypedResults.Ok(value);
    }
}
