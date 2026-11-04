using System.Text.Json.Serialization;
using Sprawl.Mcp.Clients;

namespace Sprawl.Mcp.Services;

public sealed record McpError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable,
    [property: JsonPropertyName("currentVersion")] long? CurrentVersion = null,
    [property: JsonPropertyName("entities")] IReadOnlyList<string>? Entities = null);

/// <summary>Every structured tool result: <c>ok</c>, the operation, and either <c>data</c> or an <c>error</c> with a stable code.</summary>
public sealed record McpResult<T>(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("error")] McpError? Error = null)
{
    public static McpResult<T> Success(string operation, T data) => new(true, operation, data);
    public static McpResult<T> Fail(string operation, McpError error) => new(false, operation, default, error);
}

/// <summary>Runs a tool body and maps every failure to a stable, non-leaking error. Never logs arguments or tokens.</summary>
public sealed class ToolExecutor(ILogger<ToolExecutor> logger)
{
    public async Task<McpResult<T>> Run<T>(string operation, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var (ok, value, error) = await Guard(operation, action, cancellationToken);
        return ok ? McpResult<T>.Success(operation, value!) : McpResult<T>.Fail(operation, error!);
    }

    /// <summary>For text results (the outline): the error becomes a short explanatory text.</summary>
    public async Task<(string? Text, McpError? Error)> RunText(string operation, Func<Task<string>> action, CancellationToken cancellationToken)
    {
        var (ok, value, error) = await Guard(operation, action, cancellationToken);
        return ok ? (value, null) : (null, error);
    }

    private async Task<(bool Ok, T? Value, McpError? Error)> Guard<T>(string operation, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var result = await action();
            logger.LogInformation("Tool {Operation} succeeded in {Elapsed} ms", operation, (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return (true, result, null);
        }
        catch (SprawlUpstreamException failure)
        {
            logger.LogInformation("Tool {Operation} refused upstream: {Code} ({Status})", operation, failure.Code, failure.Status);
            return (false, default, new McpError(failure.Code, failure.Message, failure.Retryable, failure.CurrentVersion, failure.Entities.Count > 0 ? failure.Entities : null));
        }
        catch (ArgumentException failure)
        {
            return (false, default, new McpError("invalid_request", failure.Message, false));
        }
        catch (UpstreamResponseTooLargeException)
        {
            logger.LogWarning("Tool {Operation} exceeded the upstream response bound", operation);
            return (false, default, new McpError("response_too_large", "The result was too large; narrow the request (for example a single region or fewer ids).", false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (false, default, new McpError("cancelled", "The operation was cancelled.", true));
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Tool {Operation} could not reach the Sprawl API: {Type}", operation, failure.GetType().Name);
            return (false, default, new McpError("upstream_unavailable", "The Sprawl API is unavailable; try again shortly.", true));
        }
    }
}
