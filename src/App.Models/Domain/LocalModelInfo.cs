namespace App.Models.Domain;

public sealed record LocalModelInfo
{
    public required string RepoId { get; init; }

    public required string DisplayName { get; init; }

    public required string LocalPath { get; init; }

    public ModelDownloadStatus Status { get; init; }

    public long DownloadedBytes { get; init; }

    public long TotalBytes { get; init; }

    public DateTimeOffset? LastValidatedAt { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsComplete => Status == ModelDownloadStatus.Downloaded;
}
