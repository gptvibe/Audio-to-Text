using App.Core.Contracts;
using App.Core.Formatting;
using App.Models.Domain;

namespace App.Services.Export;

public sealed class ExportService : IExportService
{
    private readonly TranscriptFormatter _formatter = new();

    public string FormatTranscript(TranscriptDocument transcript, TranscriptOutputMode mode)
    {
        return _formatter.Format(transcript, mode);
    }

    public async Task ExportTxtAsync(
        TranscriptDocument transcript,
        string destinationPath,
        TranscriptOutputMode mode,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath) && !overwrite)
        {
            throw new IOException($"The export file already exists: {destinationPath}");
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var text = FormatTranscript(transcript, mode);
        await File.WriteAllTextAsync(destinationPath, text, cancellationToken);
    }
}
