using App.Models.Domain;
using App.Services.History;
using App.Services.Storage;

namespace App.Tests;

[TestClass]
public sealed class HistoryServiceTests
{
    [TestMethod]
    public async Task SaveAndLoad_RoundTripsTranscript()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        var service = new HistoryService(paths);

        var item = new HistoryItem
        {
            Id = "test",
            SourceName = "meeting.wav",
            ModelRepoId = "Systran/faster-whisper-small",
            Transcript = new TranscriptDocument
            {
                Segments = [new TranscriptSegment { Text = "Hello world." }]
            }
        };

        await service.SaveAsync(item);
        var loaded = await service.GetAsync("test");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("meeting.wav", loaded.SourceName);
        Assert.AreEqual("Hello world.", loaded.Transcript.Segments[0].Text);

        Directory.Delete(root, recursive: true);
    }
}
