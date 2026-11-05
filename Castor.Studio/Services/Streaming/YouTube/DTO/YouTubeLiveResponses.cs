using System.Text.Json.Serialization;

namespace CastorApplication.Services.Streaming.YouTube.DTO;

internal sealed class LiveStreamListResponse
{
    [JsonPropertyName("items")]
    public LiveStreamResource[] Items { get; init; } = [];
}

internal sealed class LiveStreamResource
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("snippet")]
    public LiveStreamSnippet? Snippet { get; init; }

    [JsonPropertyName("cdn")]
    public LiveStreamCdn? Cdn { get; init; }

    [JsonPropertyName("contentDetails")]
    public LiveStreamContentDetails? ContentDetails { get; init; }
}

internal sealed class LiveStreamSnippet
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

internal sealed class LiveStreamCdn
{
    [JsonPropertyName("ingestionInfo")]
    public LiveStreamIngestionInfo? IngestionInfo { get; init; }
}

internal sealed class LiveStreamIngestionInfo
{
    // The stream key.
    [JsonPropertyName("streamName")]
    public string? StreamName { get; init; }

    [JsonPropertyName("ingestionAddress")]
    public string? IngestionAddress { get; init; }

    [JsonPropertyName("rtmpsIngestionAddress")]
    public string? RtmpsIngestionAddress { get; init; }
}

internal sealed class LiveStreamContentDetails
{
    [JsonPropertyName("isReusable")]
    public bool IsReusable { get; init; }
}

// {"error": {"code": 403, "message": "...", "errors": [{"reason": "liveStreamingNotEnabled"}]}}
internal sealed class GoogleApiErrorResponse
{
    [JsonPropertyName("error")]
    public GoogleApiError? Error { get; init; }
}

internal sealed class GoogleApiError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public GoogleApiErrorItem[] Errors { get; init; } = [];
}

internal sealed class GoogleApiErrorItem
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
