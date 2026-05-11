using App.Core.Contracts;

namespace App.Services.Storage;

public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuietScribe");
        ModelsDirectory = Path.Combine(RootDirectory, "models");
        DownloadsDirectory = Path.Combine(RootDirectory, "downloads");
        TempDirectory = Path.Combine(RootDirectory, "temp");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        HistoryDirectory = Path.Combine(RootDirectory, "history");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
    }

    public string RootDirectory { get; }

    public string ModelsDirectory { get; }

    public string DownloadsDirectory { get; }

    public string TempDirectory { get; }

    public string LogsDirectory { get; }

    public string HistoryDirectory { get; }

    public string SettingsPath { get; }

    public string GetModelDirectory(string repoId)
    {
        var safeName = string.Join("--", repoId.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '-');
        }

        return Path.Combine(ModelsDirectory, safeName);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(DownloadsDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(HistoryDirectory);
    }
}
