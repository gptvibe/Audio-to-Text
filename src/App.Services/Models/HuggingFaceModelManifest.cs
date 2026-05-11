using App.Models.Domain;

namespace App.Services.Models;

public sealed record HuggingFaceModelManifest
{
    public required string RepoId { get; init; }

    public DateTimeOffset DownloadedAt { get; init; } = DateTimeOffset.Now;

    public IReadOnlyList<ModelFileEntry> Files { get; init; } = [];
}
