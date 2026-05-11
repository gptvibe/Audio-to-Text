using App.Core.Contracts;
using App.Models.Domain;
using App.Services.Storage;

namespace App.Services.History;

public sealed class HistoryService : IHistoryService
{
    private readonly IAppPaths _paths;
    private readonly JsonFileStore<HistoryItem> _store = new();

    public HistoryService(IAppPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<HistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var items = new List<HistoryItem>();

        foreach (var file in Directory.EnumerateFiles(_paths.HistoryDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await _store.LoadAsync(file, cancellationToken);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items.OrderByDescending(item => item.CreatedAt).ToList();
    }

    public Task<HistoryItem?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return _store.LoadAsync(GetPath(id), cancellationToken);
    }

    public Task SaveAsync(HistoryItem item, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        return _store.SaveAsync(GetPath(item.Id), item, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = GetPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string id)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            id = id.Replace(invalid, '-');
        }

        return Path.Combine(_paths.HistoryDirectory, $"{id}.json");
    }
}
