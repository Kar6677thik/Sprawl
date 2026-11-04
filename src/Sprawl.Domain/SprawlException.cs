using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>A refused request. <see cref="Status"/> is the HTTP status the API answers with.</summary>
public sealed class SprawlException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public long? CurrentVersion { get; init; }
    public IReadOnlyList<string> Entities { get; init; } = [];

    public static SprawlException Invalid(string message) => new(ErrorCodes.InvalidRequest, message, 400);
    public static SprawlException NotFound(string message) => new(ErrorCodes.NotFound, message, 404);
    public static SprawlException Limit(string message) => new(ErrorCodes.LimitExceeded, message, 422);
    public static SprawlException Pinned(string message, IReadOnlyList<string> ids) => new(ErrorCodes.PinnedItem, message, 409) { Entities = ids };
    public static SprawlException Conflict(string message, IReadOnlyList<string> ids) => new(ErrorCodes.Conflict, message, 409) { Entities = ids };
    public static SprawlException Forbidden(string message) => new(ErrorCodes.ForbiddenForActor, message, 403);
    public static SprawlException Stale(long current) =>
        new(ErrorCodes.StaleVersion, $"The canvas changed; it is now at version {current}. Read it again and retry.", 412) { CurrentVersion = current };
}
