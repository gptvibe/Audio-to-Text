using App.Models.Domain;

namespace App.Core.Contracts;

public interface IHistoryService
{
    Task<IReadOnlyList<HistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default);

    Task<HistoryItem?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task SaveAsync(HistoryItem item, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
