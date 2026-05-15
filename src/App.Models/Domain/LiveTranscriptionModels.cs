namespace App.Models.Domain;

public enum LiveTranscriptionEventKind
{
    Ready,
    PartialText,
    FinalSegment,
    Progress,
    Error,
    Stopped
}

public enum LiveRecordingState
{
    Idle,
    LoadingModel,
    Recording,
    Paused,
    Stopping,
    Stopped,
    Failed
}

public sealed record LiveTranscriptionEvent
{
    public LiveTranscriptionEventKind Kind { get; init; }

    public string? Message { get; init; }

    public string? PartialText { get; init; }

    public TranscriptSegment? Segment { get; init; }

    public long? ChunkId { get; init; }

    public TimeSpan? AudioPosition { get; init; }

    public double? LatencyMilliseconds { get; init; }

    public string? Backend { get; init; }

    public string? ComputeType { get; init; }
}

public sealed record LiveAudioChunk
{
    public required long Id { get; init; }

    public required string Path { get; init; }

    public required TimeSpan Start { get; init; }

    public required TimeSpan Duration { get; init; }

    public bool IsFinal { get; init; }
}

public sealed record MicrophoneDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Backend { get; init; } = "WASAPI";

    public bool IsDefault { get; init; }
}
