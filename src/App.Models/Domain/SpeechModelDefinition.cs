namespace App.Models.Domain;

public sealed record SpeechModelDefinition
{
    public required string RepoId { get; init; }

    public required string DisplayName { get; init; }

    public required string Family { get; init; }

    public required string SizeEstimate { get; init; }

    public required string LanguageSupport { get; init; }

    public required string SpeedEstimate { get; init; }

    public required string QualityEstimate { get; init; }

    public bool IsRecommended { get; init; }

    public bool MayRequireToken { get; init; }

    public string? Notes { get; init; }
}
