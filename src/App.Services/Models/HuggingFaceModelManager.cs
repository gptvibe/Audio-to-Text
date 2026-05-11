using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using App.Core.Contracts;
using App.Models.Domain;
using App.Services.Settings;
using App.Services.Storage;

namespace App.Services.Models;

public sealed class HuggingFaceModelManager : IModelManager
{
    private const string ManifestFileName = "model-manifest.json";
    private readonly HttpClient _httpClient;
    private readonly IAppPaths _paths;
    private readonly IAppSettingsService _settingsService;
    private readonly JsonFileStore<HuggingFaceModelManifest> _manifestStore = new();

    public HuggingFaceModelManager(IAppPaths paths, IAppSettingsService settingsService, HttpClient? httpClient = null)
    {
        _paths = paths;
        _settingsService = settingsService;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("QuietScribe/0.1");
    }

    public IReadOnlyList<SpeechModelDefinition> GetSupportedModels()
    {
        return SpeechModelCatalog.SupportedModels;
    }

    public async Task<IReadOnlyList<LocalModelInfo>> GetDownloadedModelsAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        if (!Directory.Exists(_paths.ModelsDirectory))
        {
            return [];
        }

        var models = new List<LocalModelInfo>();
        foreach (var directory in Directory.EnumerateDirectories(_paths.ModelsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await _manifestStore.LoadAsync(Path.Combine(directory, ManifestFileName), cancellationToken);
            if (manifest is not null)
            {
                models.Add(await GetLocalModelAsync(manifest.RepoId, cancellationToken));
            }
        }

        return models
            .OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<LocalModelInfo> GetLocalModelAsync(string repoId, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var modelDirectory = _paths.GetModelDirectory(repoId);
        var definition = FindDefinition(repoId);
        var manifestPath = Path.Combine(modelDirectory, ManifestFileName);
        var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);

        if (!Directory.Exists(modelDirectory) || manifest is null)
        {
            return CreateLocalInfo(repoId, definition, modelDirectory, ModelDownloadStatus.NotDownloaded, 0, 0);
        }

        var total = manifest.Files.Sum(file => file.SizeBytes);
        var downloaded = manifest.Files.Sum(file =>
        {
            var localPath = Path.Combine(modelDirectory, file.Path);
            return File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
        });

        var isComplete = manifest.Files.Count > 0
            && manifest.Files.All(file =>
            {
                var localPath = Path.Combine(modelDirectory, file.Path);
                return File.Exists(localPath) && (file.SizeBytes <= 0 || new FileInfo(localPath).Length == file.SizeBytes);
            });

        return CreateLocalInfo(
            repoId,
            definition,
            modelDirectory,
            isComplete ? ModelDownloadStatus.Downloaded : ModelDownloadStatus.Partial,
            downloaded,
            total,
            DateTimeOffset.Now);
    }

    public async Task<LocalModelInfo> DownloadModelAsync(
        string repoId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var current = await GetLocalModelAsync(repoId, cancellationToken);
        if (current.Status == ModelDownloadStatus.Downloaded)
        {
            progress?.Report(new ModelDownloadProgress
            {
                RepoId = repoId,
                DownloadedBytes = current.DownloadedBytes,
                TotalBytes = current.TotalBytes,
                Status = ModelDownloadStatus.Downloaded,
                Message = "Model already downloaded"
            });
            return current;
        }

        var token = await _settingsService.GetHuggingFaceTokenAsync(cancellationToken);
        var files = await FetchFileListAsync(repoId, token, cancellationToken);
        if (files.Count == 0)
        {
            throw new ModelDownloadException("Hugging Face returned no model files. Check the repo ID and access permissions.");
        }

        var modelDirectory = _paths.GetModelDirectory(repoId);
        Directory.CreateDirectory(modelDirectory);

        var totalBytes = files.Sum(file => file.SizeBytes);
        var downloadedBytes = ExistingDownloadedBytes(modelDirectory, files);
        progress?.Report(new ModelDownloadProgress
        {
            RepoId = repoId,
            DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes,
            Status = ModelDownloadStatus.Downloading,
            Message = "Starting model download"
        });

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(modelDirectory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            var existingLength = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;
            if (file.SizeBytes > 0 && existingLength == file.SizeBytes)
            {
                continue;
            }

            await DownloadFileAsync(repoId, file, files, modelDirectory, destinationPath, token, totalBytes, progress, cancellationToken);
            downloadedBytes = ExistingDownloadedBytes(modelDirectory, files);
            progress?.Report(new ModelDownloadProgress
            {
                RepoId = repoId,
                CurrentFile = file.Path,
                DownloadedBytes = downloadedBytes,
                TotalBytes = totalBytes,
                Status = ModelDownloadStatus.Downloading,
                Message = $"Downloaded {file.Path}"
            });
        }

        await _manifestStore.SaveAsync(
            Path.Combine(modelDirectory, ManifestFileName),
            new HuggingFaceModelManifest { RepoId = repoId, Files = files },
            cancellationToken);

        var localModel = await GetLocalModelAsync(repoId, cancellationToken);
        if (localModel.Status != ModelDownloadStatus.Downloaded)
        {
            throw new ModelDownloadException("The model download finished, but validation failed. Try redownloading the model.");
        }

        progress?.Report(new ModelDownloadProgress
        {
            RepoId = repoId,
            DownloadedBytes = localModel.DownloadedBytes,
            TotalBytes = localModel.TotalBytes,
            Status = ModelDownloadStatus.Downloaded,
            Message = "Model ready"
        });

        return localModel;
    }

    public Task DeleteModelAsync(string repoId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelDirectory = _paths.GetModelDirectory(repoId);
        if (Directory.Exists(modelDirectory))
        {
            Directory.Delete(modelDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private async Task<IReadOnlyList<ModelFileEntry>> FetchFileListAsync(string repoId, string? token, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://huggingface.co/api/models/{repoId}/tree/main?recursive=true&expand=true");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyToken(request, token);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ModelDownloadException("This Hugging Face model requires access. Add a Hugging Face token in Settings and make sure your account can access the repo.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ModelDownloadException("Hugging Face could not find that repo ID. Check the spelling and try again.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var files = new List<ModelFileEntry>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeElement) || typeElement.GetString() != "file")
            {
                continue;
            }

            if (!item.TryGetProperty("path", out var pathElement))
            {
                continue;
            }

            var path = pathElement.GetString();
            if (string.IsNullOrWhiteSpace(path) || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var size = item.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var sizeValue)
                ? sizeValue
                : 0;

            files.Add(new ModelFileEntry { Path = path, SizeBytes = size });
        }

        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task DownloadFileAsync(
        string repoId,
        ModelFileEntry file,
        IReadOnlyList<ModelFileEntry> allFiles,
        string modelDirectory,
        string destinationPath,
        string? token,
        long totalBytes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existingLength = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;
        var uri = new Uri($"https://huggingface.co/{repoId}/resolve/main/{EncodePath(file.Path)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyToken(request, token);

        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ModelDownloadException("A Hugging Face token is required to download this model file.");
        }

        response.EnsureSuccessStatusCode();

        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
        {
            existingLength = 0;
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destinationPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        var buffer = new byte[1024 * 128];
        int read;

        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

            progress?.Report(new ModelDownloadProgress
            {
                RepoId = repoId,
                CurrentFile = file.Path,
                DownloadedBytes = Math.Min(totalBytes, ExistingDownloadedBytes(modelDirectory, allFiles)),
                TotalBytes = totalBytes,
                Status = ModelDownloadStatus.Downloading,
                Message = $"Downloading {file.Path}"
            });
        }
    }

    private static void ApplyToken(HttpRequestMessage request, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static long ExistingDownloadedBytes(string modelDirectory, IReadOnlyList<ModelFileEntry> files)
    {
        long total = 0;
        foreach (var file in files)
        {
            var path = Path.Combine(modelDirectory, file.Path);
            if (File.Exists(path))
            {
                total += file.SizeBytes > 0 ? Math.Min(new FileInfo(path).Length, file.SizeBytes) : new FileInfo(path).Length;
            }
        }

        return total;
    }

    private static string EncodePath(string path)
    {
        return string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
    }

    private static SpeechModelDefinition? FindDefinition(string repoId)
    {
        return SpeechModelCatalog.SupportedModels.FirstOrDefault(model => model.RepoId.Equals(repoId, StringComparison.OrdinalIgnoreCase));
    }

    private static LocalModelInfo CreateLocalInfo(
        string repoId,
        SpeechModelDefinition? definition,
        string modelDirectory,
        ModelDownloadStatus status,
        long downloadedBytes,
        long totalBytes,
        DateTimeOffset? lastValidatedAt = null)
    {
        return new LocalModelInfo
        {
            RepoId = repoId,
            DisplayName = definition?.DisplayName ?? repoId,
            LocalPath = modelDirectory,
            Status = status,
            DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes,
            LastValidatedAt = lastValidatedAt
        };
    }
}
