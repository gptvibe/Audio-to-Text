namespace App.Models.Domain;

public sealed record ComputeDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public ComputeDeviceKind Kind { get; init; }

    public bool IsAvailable { get; init; }

    public bool IsPreferred { get; init; }

    public string? Backend { get; init; }

    public string? Detail { get; init; }

    public int Priority { get; init; }
}
