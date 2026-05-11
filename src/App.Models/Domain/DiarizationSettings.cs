namespace App.Models.Domain;

public sealed record DiarizationSettings
{
    public bool IsEnabled { get; init; }

    public int? ExpectedSpeakers { get; init; }

    public bool MergeShortTurns { get; init; } = true;

    public TimeSpan MinimumSegmentLength { get; init; } = TimeSpan.FromSeconds(1.25);
}
