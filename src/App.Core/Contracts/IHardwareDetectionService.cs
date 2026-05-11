using App.Models.Domain;

namespace App.Core.Contracts;

public interface IHardwareDetectionService
{
    Task<IReadOnlyList<ComputeDeviceInfo>> DetectAsync(CancellationToken cancellationToken = default);
}
