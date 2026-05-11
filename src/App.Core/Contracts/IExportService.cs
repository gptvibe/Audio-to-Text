using App.Models.Domain;

namespace App.Core.Contracts;

public interface IExportService
{
    string FormatTranscript(TranscriptDocument transcript, TranscriptOutputMode mode);

    Task ExportTxtAsync(TranscriptDocument transcript, string destinationPath, TranscriptOutputMode mode, bool overwrite, CancellationToken cancellationToken = default);
}
