namespace App.Models.Domain;

public sealed record HistoryItem
{
    public required string Id { get; init; }

    public required string SourceName { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public string? ModelRepoId { get; init; }

    public string? Language { get; init; }

    public TimeSpan? Duration { get; init; }

    public bool DiarizationEnabled { get; init; }

    public string? ExportPath { get; init; }

    public TranscriptDocument Transcript { get; init; } = TranscriptDocument.Empty;
}
