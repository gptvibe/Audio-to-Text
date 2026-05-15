using App.Models.Domain;

namespace App.Services.Models;

public static class SpeechModelCatalog
{
    public static IReadOnlyList<SpeechModelDefinition> SupportedModels { get; } =
    [
        new()
        {
            RepoId = "Systran/faster-whisper-tiny",
            DisplayName = "Whisper Tiny",
            Family = "faster-whisper",
            SizeEstimate = "~75 MB",
            LanguageSupport = "Multilingual",
            SpeedEstimate = "Very fast",
            QualityEstimate = "Basic",
            Notes = "Best for low-latency live transcription on CPU-bound machines."
        },
        new()
        {
            RepoId = "Systran/faster-whisper-base",
            DisplayName = "Whisper Base",
            Family = "faster-whisper",
            SizeEstimate = "~150 MB",
            LanguageSupport = "Multilingual",
            SpeedEstimate = "Very fast",
            QualityEstimate = "Fair",
            Notes = "A practical live transcription model when speed matters more than maximum accuracy."
        },
        new()
        {
            RepoId = "Systran/faster-whisper-small",
            DisplayName = "Whisper Small",
            Family = "faster-whisper",
            SizeEstimate = "~460 MB",
            LanguageSupport = "Multilingual",
            SpeedEstimate = "Fast",
            QualityEstimate = "Good",
            IsRecommended = true,
            Notes = "A strong first download for laptops and general transcription."
        },
        new()
        {
            RepoId = "Systran/faster-whisper-medium",
            DisplayName = "Whisper Medium",
            Family = "faster-whisper",
            SizeEstimate = "~1.5 GB",
            LanguageSupport = "Multilingual",
            SpeedEstimate = "Balanced",
            QualityEstimate = "Very good",
            Notes = "Better accuracy for meetings and long-form audio, with higher memory use."
        },
        new()
        {
            RepoId = "Systran/faster-whisper-large-v3",
            DisplayName = "Whisper Large v3",
            Family = "faster-whisper",
            SizeEstimate = "~3.1 GB",
            LanguageSupport = "Multilingual",
            SpeedEstimate = "Slower",
            QualityEstimate = "Excellent",
            Notes = "Best quality among the default local models, but needs more RAM/VRAM."
        },
        new()
        {
            RepoId = "Systran/faster-distil-whisper-large-v3",
            DisplayName = "Distil Whisper Large v3",
            Family = "faster-whisper",
            SizeEstimate = "~1.5 GB",
            LanguageSupport = "Multilingual",
            SpeedEstimate = "Very fast",
            QualityEstimate = "Very good",
            Notes = "A good turbo-style model when speed matters."
        },
        new()
        {
            RepoId = "openai/whisper-small",
            DisplayName = "OpenAI Whisper Small",
            Family = "whisper",
            SizeEstimate = "~970 MB",
            LanguageSupport = "Multilingual",
            SpeedEstimate = "Fast",
            QualityEstimate = "Good",
            Notes = "Reference Whisper model. The v1 worker prefers faster-whisper converted repos."
        }
    ];
}
