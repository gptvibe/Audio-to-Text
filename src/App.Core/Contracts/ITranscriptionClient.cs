using App.Models.Domain;

namespace App.Core.Contracts;

public interface ITranscriptionClient
{
    Task<TranscriptDocument> TranscribeFileAsync(
        string sourcePath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<TranscriptionProgress>? progress = null,
        IProgress<TranscriptSegment>? segmentProgress = null,
        CancellationToken cancellationToken = default);
}
