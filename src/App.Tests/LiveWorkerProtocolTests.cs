using App.Inference.Worker;
using App.Models.Domain;

namespace App.Tests;

[TestClass]
public sealed class LiveWorkerProtocolTests
{
    [TestMethod]
    public void ParseLivePartial_ReturnsPartialTextEvent()
    {
        var liveEvent = LiveWorkerProtocol.ParseLiveEvent(
            "{\"event\":\"live_partial\",\"chunk_id\":4,\"text\":\"hello from the mic\",\"audio_position\":7.5,\"latency_ms\":420}");

        Assert.AreEqual(LiveTranscriptionEventKind.PartialText, liveEvent.Kind);
        Assert.AreEqual("hello from the mic", liveEvent.PartialText);
        Assert.AreEqual(4, liveEvent.ChunkId);
        Assert.AreEqual(TimeSpan.FromSeconds(7.5), liveEvent.AudioPosition);
        Assert.AreEqual(420, liveEvent.LatencyMilliseconds);
    }

    [TestMethod]
    public void ParseLiveSegment_ReturnsTranscriptSegment()
    {
        var liveEvent = LiveWorkerProtocol.ParseLiveEvent(
            "{\"event\":\"live_segment\",\"segment\":{\"start\":1.25,\"end\":2.5,\"text\":\"stable line\"}}");

        Assert.AreEqual(LiveTranscriptionEventKind.FinalSegment, liveEvent.Kind);
        Assert.IsNotNull(liveEvent.Segment);
        Assert.AreEqual(TimeSpan.FromSeconds(1.25), liveEvent.Segment.Start);
        Assert.AreEqual(TimeSpan.FromSeconds(2.5), liveEvent.Segment.End);
        Assert.AreEqual("stable line", liveEvent.Segment.Text);
    }

    [TestMethod]
    public void BuildChunkCommand_UsesLiveJsonCommand()
    {
        var command = LiveWorkerProtocol.BuildChunkCommand(new LiveAudioChunk
        {
            Id = 7,
            Path = @"C:\Temp\chunk.wav",
            Start = TimeSpan.FromSeconds(2),
            Duration = TimeSpan.FromSeconds(4),
            IsFinal = true
        });

        StringAssert.Contains(command, "\"command\":\"chunk\"");
        StringAssert.Contains(command, "\"id\":7");
        StringAssert.Contains(command, "\"is_final\":true");
    }
}
