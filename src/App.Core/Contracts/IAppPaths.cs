namespace App.Core.Contracts;

public interface IAppPaths
{
    string RootDirectory { get; }

    string ModelsDirectory { get; }

    string DownloadsDirectory { get; }

    string TempDirectory { get; }

    string LogsDirectory { get; }

    string HistoryDirectory { get; }

    string SettingsPath { get; }

    string GetModelDirectory(string repoId);

    void EnsureCreated();
}
