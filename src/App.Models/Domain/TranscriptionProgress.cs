namespace App.Models.Domain;

public sealed record TranscriptionProgress
{
    public TranscriptionStage Stage { get; init; } = TranscriptionStage.Idle;

    public double? Percent { get; init; }

    public string Message { get; init; } = "Ready";

    public TimeSpan? EstimatedRemaining { get; init; }
}
