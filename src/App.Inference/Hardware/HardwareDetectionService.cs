using System.Diagnostics;
using App.Core.Contracts;
using App.Models.Domain;

namespace App.Inference.Hardware;

public sealed class HardwareDetectionService : IHardwareDetectionService
{
    public async Task<IReadOnlyList<ComputeDeviceInfo>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<ComputeDeviceInfo>
        {
            new()
            {
                Id = "cpu",
                Name = $"{Environment.ProcessorCount} logical CPU cores",
                Kind = ComputeDeviceKind.Cpu,
                IsAvailable = true,
                Backend = "faster-whisper CPU",
                Detail = "Always available fallback",
                Priority = 90
            }
        };

        await DetectNvidiaAsync(devices, cancellationToken);
        await DetectDisplayAdaptersAsync(devices, cancellationToken);
        await DetectIntelNpuAsync(devices, cancellationToken);

        var preferred = devices
            .Where(device => device.IsAvailable)
            .OrderBy(device => device.Priority)
            .FirstOrDefault();

        return devices
            .Select(device => device == preferred ? device with { IsPreferred = true } : device)
            .OrderBy(device => device.Priority)
            .ToList();
    }

    private static async Task DetectNvidiaAsync(List<ComputeDeviceInfo> devices, CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunProcessAsync("nvidia-smi", "--query-gpu=name,driver_version --format=csv,noheader", cancellationToken);
            var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var line in lines)
            {
                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                var name = parts.Length > 0 ? parts[0] : "NVIDIA GPU";
                var driver = parts.Length > 1 ? parts[1] : null;
                devices.Add(new ComputeDeviceInfo
                {
                    Id = $"cuda-{devices.Count}",
                    Name = name,
                    Kind = ComputeDeviceKind.NvidiaGpu,
                    IsAvailable = true,
                    Backend = "CUDA",
                    Detail = string.IsNullOrWhiteSpace(driver) ? "CUDA capable device detected" : $"Driver {driver}",
                    Priority = 20
                });
            }
        }
        catch
        {
            // GPU detection is advisory. The app should remain usable on CPU.
        }
    }

    private static async Task DetectDisplayAdaptersAsync(List<ComputeDeviceInfo> devices, CancellationToken cancellationToken)
    {
        try
        {
            var command = "Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name";
            var output = await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"", cancellationToken);
            var names = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (devices.Any(device => name.Contains(device.Name, StringComparison.OrdinalIgnoreCase)
                                          || device.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var kind = ClassifyDisplayAdapter(name);
                var backend = kind switch
                {
                    ComputeDeviceKind.AmdGpu => "DirectML candidate",
                    ComputeDeviceKind.IntelGpu => "OpenVINO candidate",
                    ComputeDeviceKind.NvidiaGpu => "CUDA candidate",
                    _ => "Unknown"
                };

                devices.Add(new ComputeDeviceInfo
                {
                    Id = $"display-{devices.Count}",
                    Name = name,
                    Kind = kind,
                    IsAvailable = kind != ComputeDeviceKind.UnknownAccelerator,
                    Backend = backend,
                    Detail = kind is ComputeDeviceKind.AmdGpu or ComputeDeviceKind.IntelGpu
                        ? "Detected display adapter. Backend support depends on installed runtime."
                        : "Detected display adapter",
                    Priority = kind switch
                    {
                        ComputeDeviceKind.NvidiaGpu => 25,
                        ComputeDeviceKind.IntelGpu => 35,
                        ComputeDeviceKind.AmdGpu => 40,
                        _ => 80
                    }
                });
            }
        }
        catch
        {
            // Advisory only.
        }
    }

    private static async Task DetectIntelNpuAsync(List<ComputeDeviceInfo> devices, CancellationToken cancellationToken)
    {
        try
        {
            var command = "Get-PnpDevice -PresentOnly | Where-Object { $_.FriendlyName -match 'NPU|AI Boost|Neural' } | Select-Object -ExpandProperty FriendlyName";
            var output = await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"", cancellationToken);
            var names = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                devices.Add(new ComputeDeviceInfo
                {
                    Id = $"npu-{devices.Count}",
                    Name = name,
                    Kind = ComputeDeviceKind.IntelNpu,
                    IsAvailable = true,
                    Backend = "OpenVINO candidate",
                    Detail = "NPU detected. v1 reports availability; optimized inference can be added later.",
                    Priority = 10
                });
            }
        }
        catch
        {
            // NPU support is nice-to-have and never blocks the app.
        }
    }

    private static ComputeDeviceKind ClassifyDisplayAdapter(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return ComputeDeviceKind.NvidiaGpu;
        }

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return ComputeDeviceKind.AmdGpu;
        }

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return ComputeDeviceKind.IntelGpu;
        }

        return ComputeDeviceKind.UnknownAccelerator;
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var waitTask = process.WaitForExitAsync(cancellationToken);
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(4), cancellationToken));

        if (completed != waitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup.
            }

            return string.Empty;
        }

        return await outputTask;
    }
}
