namespace App.Models.Domain;

public sealed record AppSettings
{
    public AppThemePreference Theme { get; init; } = AppThemePreference.System;

    public PerformanceMode PerformanceMode { get; init; } = PerformanceMode.Auto;

    public string? DefaultModelRepoId { get; init; }

    public string? LastExportFolder { get; init; }

    public bool HasCompletedOnboarding { get; init; }

    public bool HasHuggingFaceToken { get; init; }

    public DiarizationSettings Diarization { get; init; } = new();

    public string PrivacyNotice { get; init; } = "Your audio stays on this device. Network access is only used for model downloads.";
}
