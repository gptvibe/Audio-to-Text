namespace App.Models.Domain;

public sealed record TranscriptDocument
{
    public static TranscriptDocument Empty { get; } = new();

    public string? SourcePath { get; init; }

    public string? SourceName { get; init; }

    public string? Language { get; init; }

    public string? ModelRepoId { get; init; }

    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];

    public IReadOnlyDictionary<string, string> SpeakerNames { get; init; } = new Dictionary<string, string>();

    public TimeSpan? Duration { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public bool HasText => Segments.Count > 0 && Segments.Any(segment => !string.IsNullOrWhiteSpace(segment.Text));
}
