using App.Core.Contracts;
using App.Inference.Hardware;
using App.Inference.Worker;
using App.Services.Diagnostics;
using App.Services.Export;
using App.Services.History;
using App.Services.Models;
using App.Services.Security;
using App.Services.Settings;
using App.Services.Storage;

namespace App_Desktop;

public static class AppServices
{
    static AppServices()
    {
        Paths.EnsureCreated();
    }

    public static IAppPaths Paths { get; } = new AppPaths();

    public static ISecretStore SecretStore { get; } = new WindowsCredentialStore();

    public static IAppSettingsService Settings { get; } = new AppSettingsService(Paths, SecretStore);

    public static IHardwareDetectionService HardwareDetection { get; } = new HardwareDetectionService();

    public static IModelManager ModelManager { get; } = new HuggingFaceModelManager(Paths, Settings);

    public static ITranscriptionClient TranscriptionClient { get; } = new TranscriptionWorkerClient();

    public static ILiveTranscriptionClient LiveTranscriptionClient { get; } = new TranscriptionWorkerClient();

    public static IExportService ExportService { get; } = new ExportService();

    public static IHistoryService HistoryService { get; } = new HistoryService(Paths);

    public static IDiagnosticInfoProvider Diagnostics { get; } = new DiagnosticInfoProvider(Paths, HardwareDetection);
}
