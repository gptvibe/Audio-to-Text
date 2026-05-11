using App.Models.Domain;

namespace App.Core.Contracts;

public sealed record ModelDownloadProgress
{
    public string RepoId { get; init; } = string.Empty;

    public string? CurrentFile { get; init; }

    public long DownloadedBytes { get; init; }

    public long TotalBytes { get; init; }

    public ModelDownloadStatus Status { get; init; } = ModelDownloadStatus.NotDownloaded;

    public string Message { get; init; } = "Waiting";

    public double? Percent => TotalBytes <= 0 ? null : Math.Clamp(DownloadedBytes / (double)TotalBytes * 100d, 0d, 100d);
}
