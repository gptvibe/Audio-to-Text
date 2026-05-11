using System.Reflection;
using System.Text;
using App.Core.Contracts;

namespace App.Services.Diagnostics;

public sealed class DiagnosticInfoProvider : IDiagnosticInfoProvider
{
    private readonly IAppPaths _paths;
    private readonly IHardwareDetectionService _hardwareDetectionService;

    public DiagnosticInfoProvider(IAppPaths paths, IHardwareDetectionService hardwareDetectionService)
    {
        _paths = paths;
        _hardwareDetectionService = hardwareDetectionService;
    }

    public async Task<string> CreateDiagnosticInfoAsync(CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.AppendLine("QuietScribe diagnostics");
        builder.AppendLine($"Generated: {DateTimeOffset.Now:O}");
        builder.AppendLine($"App version: {Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "development"}");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($".NET: {Environment.Version}");
        builder.AppendLine($"Process: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        builder.AppendLine($"App data: {_paths.RootDirectory}");
        builder.AppendLine();
        builder.AppendLine("Detected devices:");

        try
        {
            var devices = await _hardwareDetectionService.DetectAsync(cancellationToken);
            foreach (var device in devices)
            {
                builder.AppendLine($"- {device.Name} [{device.Kind}] {(device.IsAvailable ? "available" : "not available")} {device.Detail}");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"- Device detection failed: {ex.Message}");
        }

        return builder.ToString();
    }
}
