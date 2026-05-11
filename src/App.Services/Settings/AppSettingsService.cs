using App.Core.Contracts;
using App.Models.Domain;
using App.Services.Security;
using App.Services.Storage;

namespace App.Services.Settings;

public sealed class AppSettingsService : IAppSettingsService
{
    private const string HuggingFaceTokenKey = "QuietScribe.HuggingFaceToken";
    private readonly IAppPaths _paths;
    private readonly JsonFileStore<AppSettings> _store;
    private readonly ISecretStore _secretStore;

    public AppSettingsService(IAppPaths paths, ISecretStore secretStore)
    {
        _paths = paths;
        _secretStore = secretStore;
        _store = new JsonFileStore<AppSettings>();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var settings = await _store.LoadAsync(_paths.SettingsPath, cancellationToken) ?? new AppSettings();
        var token = await _secretStore.ReadSecretAsync(HuggingFaceTokenKey, cancellationToken);
        return settings with { HasHuggingFaceToken = !string.IsNullOrWhiteSpace(token) };
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        return _store.SaveAsync(_paths.SettingsPath, settings, cancellationToken);
    }

    public async Task SaveHuggingFaceTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            await ClearHuggingFaceTokenAsync(cancellationToken);
            return;
        }

        await _secretStore.SaveSecretAsync(HuggingFaceTokenKey, token.Trim(), cancellationToken);
        var settings = await LoadAsync(cancellationToken);
        await SaveAsync(settings with { HasHuggingFaceToken = true }, cancellationToken);
    }

    public Task<string?> GetHuggingFaceTokenAsync(CancellationToken cancellationToken = default)
    {
        return _secretStore.ReadSecretAsync(HuggingFaceTokenKey, cancellationToken);
    }

    public async Task ClearHuggingFaceTokenAsync(CancellationToken cancellationToken = default)
    {
        await _secretStore.DeleteSecretAsync(HuggingFaceTokenKey, cancellationToken);
        var settings = await LoadAsync(cancellationToken);
        await SaveAsync(settings with { HasHuggingFaceToken = false }, cancellationToken);
    }
}
