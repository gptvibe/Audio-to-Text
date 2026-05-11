using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using App.Core.Contracts;
using App.Models.Domain;

namespace App.Inference.Worker;

public sealed class TranscriptionWorkerClient : ITranscriptionClient
{
    private readonly string _workerScriptPath;
    private readonly string _pythonExecutable;

    public TranscriptionWorkerClient(string? workerScriptPath = null, string? pythonExecutable = null)
    {
        _workerScriptPath = workerScriptPath ?? LocateWorkerScript();
        _pythonExecutable = pythonExecutable ?? LocatePythonExecutable(_workerScriptPath);
    }

    public async Task<TranscriptDocument> TranscribeFileAsync(
        string sourcePath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<TranscriptionProgress>? progress = null,
        IProgress<TranscriptSegment>? segmentProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected audio or video file could not be found.", sourcePath);
        }

        if (!Directory.Exists(modelPath))
        {
            throw new DirectoryNotFoundException("The selected transcription model is missing. Download or redownload the model first.");
        }

        if (!File.Exists(_workerScriptPath))
        {
            throw new FileNotFoundException("The transcription worker script is missing.", _workerScriptPath);
        }

        progress?.Report(new TranscriptionProgress { Stage = TranscriptionStage.LoadingModel, Message = "Starting transcription worker" });

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _pythonExecutable,
            Arguments = BuildArguments(sourcePath, modelPath, options),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_workerScriptPath) ?? AppContext.BaseDirectory
        };
        ConfigurePythonEnvironment(process.StartInfo);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new TranscriptionWorkerException(
                $"The transcription runtime could not be started at '{_pythonExecutable}'. Reinstall QuietScribe or use the portable release with the bundled worker runtime.",
                ex);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cancellation.
            }
        });

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        TranscriptDocument? transcript = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var eventType = root.TryGetProperty("event", out var eventElement) ? eventElement.GetString() : null;

            switch (eventType)
            {
                case "progress":
                    progress?.Report(ParseProgress(root));
                    break;
                case "segment":
                    segmentProgress?.Report(ParseSegment(root));
                    break;
                case "result":
                    transcript = ParseTranscript(root, sourcePath, options);
                    break;
                case "error":
                    throw new TranscriptionWorkerException(root.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString() ?? "The transcription worker failed."
                        : "The transcription worker failed.");
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new TranscriptionWorkerException(string.IsNullOrWhiteSpace(stderr)
                ? "The transcription worker exited with an error."
                : stderr.Trim());
        }

        if (transcript is null)
        {
            throw new TranscriptionWorkerException("The transcription worker completed without returning a transcript.");
        }

        progress?.Report(new TranscriptionProgress
        {
            Stage = TranscriptionStage.Completed,
            Percent = 100,
            Message = "Transcript ready"
        });

        return transcript;
    }

    private string BuildArguments(string sourcePath, string modelPath, TranscriptionOptions options)
    {
        var args = new List<string>
        {
            Quote(_workerScriptPath),
            "transcribe",
            "--input",
            Quote(sourcePath),
            "--model",
            Quote(modelPath),
            "--mode",
            options.PerformanceMode.ToString(),
            "--output-mode",
            options.OutputMode.ToString()
        };

        if (!string.IsNullOrWhiteSpace(options.Language))
        {
            args.Add("--language");
            args.Add(Quote(options.Language));
        }

        if (options.TranslateToEnglish)
        {
            args.Add("--translate");
        }

        if (options.Diarization.IsEnabled)
        {
            args.Add("--diarize");
            if (options.Diarization.ExpectedSpeakers is not null)
            {
                args.Add("--speakers");
                args.Add(options.Diarization.ExpectedSpeakers.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        return string.Join(" ", args);
    }

    private static TranscriptionProgress ParseProgress(JsonElement root)
    {
        var stageText = root.TryGetProperty("stage", out var stageElement) ? stageElement.GetString() : null;
        var stage = Enum.TryParse<TranscriptionStage>(stageText, ignoreCase: true, out var parsedStage)
            ? parsedStage
            : TranscriptionStage.Transcribing;

        var percent = root.TryGetProperty("percent", out var percentElement) && percentElement.TryGetDouble(out var percentValue)
            ? percentValue
            : (double?)null;

        var message = root.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? stage.ToString()
            : stage.ToString();

        return new TranscriptionProgress { Stage = stage, Percent = percent, Message = message };
    }

    private static TranscriptDocument ParseTranscript(JsonElement root, string sourcePath, TranscriptionOptions options)
    {
        var language = root.TryGetProperty("language", out var languageElement) ? languageElement.GetString() : options.Language;
        var duration = root.TryGetProperty("duration", out var durationElement) && durationElement.TryGetDouble(out var durationSeconds)
            ? TimeSpan.FromSeconds(durationSeconds)
            : (TimeSpan?)null;
        var segments = new List<TranscriptSegment>();

        if (root.TryGetProperty("segments", out var segmentsElement))
        {
            foreach (var segmentElement in segmentsElement.EnumerateArray())
            {
                segments.Add(ParseSegmentElement(segmentElement));
            }
        }

        return new TranscriptDocument
        {
            SourcePath = sourcePath,
            SourceName = Path.GetFileName(sourcePath),
            Language = language,
            ModelRepoId = options.ModelRepoId,
            Duration = duration,
            Segments = segments,
            SpeakerNames = segments
                .Select(segment => segment.Speaker)
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(speaker => speaker!, speaker => speaker!, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static TranscriptSegment ParseSegment(JsonElement root)
    {
        if (root.TryGetProperty("segment", out var segmentElement))
        {
            return ParseSegmentElement(segmentElement);
        }

        return ParseSegmentElement(root);
    }

    private static TranscriptSegment ParseSegmentElement(JsonElement segmentElement)
    {
        var text = segmentElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
        var start = segmentElement.TryGetProperty("start", out var startElement) && startElement.TryGetDouble(out var startSeconds)
            ? TimeSpan.FromSeconds(startSeconds)
            : (TimeSpan?)null;
        var end = segmentElement.TryGetProperty("end", out var endElement) && endElement.TryGetDouble(out var endSeconds)
            ? TimeSpan.FromSeconds(endSeconds)
            : (TimeSpan?)null;
        var speaker = segmentElement.TryGetProperty("speaker", out var speakerElement)
            ? speakerElement.GetString()
            : null;

        return new TranscriptSegment { Start = start, End = end, Speaker = speaker, Text = text };
    }

    private static string LocateWorkerScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "workers", "transcription-worker", "worker.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "workers", "transcription-worker", "worker.py"),
            Path.Combine(Environment.CurrentDirectory, "workers", "transcription-worker", "worker.py")
        };

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string LocatePythonExecutable(string workerScriptPath)
    {
        var workerDirectory = Path.GetDirectoryName(workerScriptPath) ?? AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(workerDirectory, "python", "python.exe"),
            Path.Combine(workerDirectory, ".venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "python", "python.exe")
        };

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) ?? "python";
    }

    private void ConfigurePythonEnvironment(ProcessStartInfo startInfo)
    {
        var pythonDirectory = Path.GetDirectoryName(_pythonExecutable);
        if (!string.IsNullOrWhiteSpace(pythonDirectory) && Directory.Exists(pythonDirectory))
        {
            var existingPath = startInfo.Environment.TryGetValue("PATH", out var value)
                ? value
                : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = string.IsNullOrWhiteSpace(existingPath)
                ? pythonDirectory
                : pythonDirectory + Path.PathSeparator + existingPath;
            startInfo.Environment["PYTHONNOUSERSITE"] = "1";
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
