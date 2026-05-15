using App.Core.Live;
using App.Models.Domain;

namespace App.Tests;

[TestClass]
public sealed class LiveTranscriptMergerTests
{
    [TestMethod]
    public void RemoveOverlap_RemovesRepeatedBoundaryWords()
    {
        var deduped = OverlapTextDeduplicator.RemoveOverlap(
            "We are testing live transcription",
            "live transcription with overlap");

        Assert.AreEqual("with overlap", deduped);
    }

    [TestMethod]
    public void AddFinalSegment_SkipsDuplicateOverlap()
    {
        var merger = new LiveTranscriptMerger();
        merger.AddFinalSegment(new TranscriptSegment
        {
            Start = TimeSpan.Zero,
            End = TimeSpan.FromSeconds(2),
            Text = "Hello world"
        });

        var added = merger.AddFinalSegment(new TranscriptSegment
        {
            Start = TimeSpan.FromSeconds(1.5),
            End = TimeSpan.FromSeconds(3),
            Text = "world again"
        });

        Assert.IsTrue(added);
        Assert.HasCount(2, merger.Segments);
        Assert.AreEqual("again", merger.Segments[1].Text);
    }

    [TestMethod]
    public void Apply_StoppedClearsPartialText()
    {
        var merger = new LiveTranscriptMerger();
        merger.Apply(new LiveTranscriptionEvent
        {
            Kind = LiveTranscriptionEventKind.PartialText,
            PartialText = "temporary words"
        });

        merger.Apply(new LiveTranscriptionEvent { Kind = LiveTranscriptionEventKind.Stopped });

        Assert.AreEqual(string.Empty, merger.PartialText);
    }
}
