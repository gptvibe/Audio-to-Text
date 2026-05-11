using App.Models.Domain;

namespace App.Core.Contracts;

public interface IModelManager
{
    IReadOnlyList<SpeechModelDefinition> GetSupportedModels();

    Task<IReadOnlyList<LocalModelInfo>> GetDownloadedModelsAsync(CancellationToken cancellationToken = default);

    Task<LocalModelInfo> GetLocalModelAsync(string repoId, CancellationToken cancellationToken = default);

    Task<LocalModelInfo> DownloadModelAsync(
        string repoId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task DeleteModelAsync(string repoId, CancellationToken cancellationToken = default);
}
