using App.Inference.Worker;
using App.Models.Domain;

namespace App.Tests;

[TestClass]
public sealed class LiveTranscriptionCancellationTests
{
    [TestMethod]
    public async Task StartLiveSessionAsync_WhenCanceled_StopsBeforeStartingWorker()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var client = new TranscriptionWorkerClient("missing-worker.py", "python");

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            client.StartLiveSessionAsync(
                "missing-model",
                new TranscriptionOptions { ModelRepoId = "Systran/faster-whisper-small" },
                cancellationToken: cts.Token));
    }
}
