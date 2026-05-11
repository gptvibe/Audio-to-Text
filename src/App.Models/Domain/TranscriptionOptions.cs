namespace App.Models.Domain;

public sealed record TranscriptionOptions
{
    public required string ModelRepoId { get; init; }

    public string? Language { get; init; }

    public bool TranslateToEnglish { get; init; }

    public TranscriptOutputMode OutputMode { get; init; } = TranscriptOutputMode.PlainText;

    public PerformanceMode PerformanceMode { get; init; } = PerformanceMode.Auto;

    public DiarizationSettings Diarization { get; init; } = new();
}
