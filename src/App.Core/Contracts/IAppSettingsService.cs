using App.Models.Domain;

namespace App.Core.Contracts;

public interface IAppSettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task SaveHuggingFaceTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<string?> GetHuggingFaceTokenAsync(CancellationToken cancellationToken = default);

    Task ClearHuggingFaceTokenAsync(CancellationToken cancellationToken = default);
}
