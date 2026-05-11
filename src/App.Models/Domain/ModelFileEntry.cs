namespace App.Models.Domain;

public sealed record ModelFileEntry
{
    public required string Path { get; init; }

    public long SizeBytes { get; init; }

    public string? Sha256 { get; init; }
}
