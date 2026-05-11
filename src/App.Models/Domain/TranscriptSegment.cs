namespace App.Models.Domain;

public sealed record TranscriptSegment
{
    public TimeSpan? Start { get; init; }

    public TimeSpan? End { get; init; }

    public required string Text { get; init; }

    public string? Speaker { get; init; }
}
