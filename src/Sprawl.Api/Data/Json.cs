using System.IO.Compression;
using System.Text.Json;
using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Api.Data;

public static class SprawlJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.AllowOutOfOrderMetadataProperties = true;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        return options;
    }

    public static string? Entity(IEntity? entity) => entity is null ? null : JsonSerializer.Serialize(entity, entity.GetType(), Options);

    public static IEntity? ReadEntity(string entityId, string? json) => json is null ? null : EntityIds.KindOf(entityId) switch
    {
        EntityKind.Canvas => JsonSerializer.Deserialize<CanvasMeta>(json, Options),
        EntityKind.Region => JsonSerializer.Deserialize<Region>(json, Options),
        EntityKind.Card => JsonSerializer.Deserialize<Card>(json, Options),
        EntityKind.Connection => JsonSerializer.Deserialize<Connection>(json, Options),
        _ => throw new InvalidDataException($"Unknown entity id '{entityId}' in the op log.")
    };

    public static JsonElement? Element(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();

    public static byte[] Compress(CanvasSnapshot snapshot)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(gzip, snapshot, Options);
        return buffer.ToArray();
    }

    public static CanvasSnapshot Decompress(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<CanvasSnapshot>(gzip, Options) ?? throw new InvalidDataException("Empty snapshot.");
    }
}
