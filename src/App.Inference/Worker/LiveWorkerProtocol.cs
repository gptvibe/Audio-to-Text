using System.Text.Json;
using App.Models.Domain;

namespace App.Inference.Worker;

public static class LiveWorkerProtocol
{
    public static string BuildChunkCommand(LiveAudioChunk chunk)
    {
        var payload = new Dictionary<string, object?>
        {
            ["command"] = "chunk",
            ["id"] = chunk.Id,
            ["path"] = chunk.Path,
            ["start"] = chunk.Start.TotalSeconds,
            ["duration"] = chunk.Duration.TotalSeconds,
            ["is_final"] = chunk.IsFinal
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string BuildStopCommand()
    {
        return "{\"command\":\"stop\"}";
    }

    public static LiveTranscriptionEvent ParseLiveEvent(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var eventType = root.TryGetProperty("event", out var eventElement)
            ? eventElement.GetString()
            : null;

        return eventType switch
        {
            "ready" => new LiveTranscriptionEvent
            {
                Kind = LiveTranscriptionEventKind.Ready,
                Message = GetString(root, "message") ?? "Live worker ready",
                Backend = GetString(root, "backend"),
                ComputeType = GetString(root, "compute_type")
            },
            "live_partial" => new LiveTranscriptionEvent
            {
                Kind = LiveTranscriptionEventKind.PartialText,
                Message = GetString(root, "message"),
                PartialText = GetString(root, "text") ?? string.Empty,
                ChunkId = GetInt64(root, "chunk_id"),
                AudioPosition = GetSeconds(root, "audio_position"),
                LatencyMilliseconds = GetDouble(root, "latency_ms")
            },
            "live_segment" => new LiveTranscriptionEvent
            {
                Kind = LiveTranscriptionEventKind.FinalSegment,
                Message = GetString(root, "message"),
                Segment = root.TryGetProperty("segment", out var segmentElement)
                    ? ParseSegment(segmentElement)
                    : ParseSegment(root),
                ChunkId = GetInt64(root, "chunk_id"),
                AudioPosition = GetSeconds(root, "audio_position"),
                LatencyMilliseconds = GetDouble(root, "latency_ms")
            },
            "live_progress" => new LiveTranscriptionEvent
            {
                Kind = LiveTranscriptionEventKind.Progress,
                Message = GetString(root, "message") ?? "Live transcription update",
                ChunkId = GetInt64(root, "chunk_id"),
                AudioPosition = GetSeconds(root, "audio_position"),
                LatencyMilliseconds = GetDouble(root, "latency_ms"),
                Backend = GetString(root, "backend"),
                ComputeType = GetString(root, "compute_type")
            },
            "live_error" or "error" => new LiveTranscriptionEvent
            {
                Kind = LiveTranscriptionEventKind.Error,
                Message = GetString(root, "message") ?? "The live transcription worker failed.",
                ChunkId = GetInt64(root, "chunk_id")
            },
            "live_stopped" => new LiveTranscriptionEvent
            {
                Kind = LiveTranscriptionEventKind.Stopped,
                Message = GetString(root, "message") ?? "Live transcription stopped",
                AudioPosition = GetSeconds(root, "audio_position")
            },
            _ => new LiveTranscriptionEvent
            {
                Kind = LiveTranscriptionEventKind.Progress,
                Message = string.IsNullOrWhiteSpace(eventType) ? "Live worker event" : eventType
            }
        };
    }

    private static TranscriptSegment ParseSegment(JsonElement segmentElement)
    {
        var text = GetString(segmentElement, "text") ?? string.Empty;
        return new TranscriptSegment
        {
            Text = text,
            Start = GetSeconds(segmentElement, "start"),
            End = GetSeconds(segmentElement, "end"),
            Speaker = GetString(segmentElement, "speaker")
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
    }

    private static long? GetInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static TimeSpan? GetSeconds(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
            ? TimeSpan.FromSeconds(value)
            : null;
    }
}
