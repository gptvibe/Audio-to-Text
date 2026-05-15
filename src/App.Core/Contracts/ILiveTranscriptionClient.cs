using App.Models.Domain;

namespace App.Core.Contracts;

public interface ILiveTranscriptionClient
{
    Task<ILiveTranscriptionSession> StartLiveSessionAsync(
        string modelPath,
        TranscriptionOptions options,
        IProgress<LiveTranscriptionEvent>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface ILiveTranscriptionSession : IAsyncDisposable
{
    Task Ready { get; }

    bool IsReady { get; }

    Task SendAudioChunkAsync(LiveAudioChunk chunk, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
