namespace App.Models.Domain;

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public enum PerformanceMode
{
    Auto,
    BestAccuracy,
    Fastest,
    LowPower,
    CpuOnly
}

public enum ComputeDeviceKind
{
    Cpu,
    NvidiaGpu,
    AmdGpu,
    IntelGpu,
    IntelNpu,
    UnknownAccelerator
}

public enum ModelDownloadStatus
{
    NotDownloaded,
    Partial,
    Downloading,
    Downloaded,
    Invalid,
    Error
}

public enum TranscriptionStage
{
    Idle,
    LoadingModel,
    PreparingAudio,
    Transcribing,
    DetectingSpeakers,
    FinalizingTranscript,
    Completed,
    Canceled,
    Failed
}

public enum TranscriptOutputMode
{
    PlainText,
    Timestamps,
    Speakers,
    SpeakersAndTimestamps
}

public enum ExportFormat
{
    Txt,
    Srt,
    Vtt,
    Json,
    Markdown
}
