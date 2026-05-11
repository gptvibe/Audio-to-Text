using App.Core.Formatting;
using App.Models.Domain;

namespace App.Tests;

[TestClass]
public sealed class TranscriptFormatterTests
{
    [TestMethod]
    public void Format_WithSpeakersAndTimestamps_UsesFriendlySpeakerNames()
    {
        var transcript = new TranscriptDocument
        {
            SpeakerNames = new Dictionary<string, string> { ["Speaker 1"] = "John" },
            Segments =
            [
                new TranscriptSegment
                {
                    Start = TimeSpan.FromSeconds(3),
                    End = TimeSpan.FromSeconds(7),
                    Speaker = "Speaker 1",
                    Text = " Hello there. "
                }
            ]
        };

        var formatted = new TranscriptFormatter().Format(transcript, TranscriptOutputMode.SpeakersAndTimestamps);

        Assert.AreEqual("[00:03 - 00:07] John: Hello there.", formatted);
    }

    [TestMethod]
    public void Format_PlainText_DoesNotIncludeMetadata()
    {
        var transcript = new TranscriptDocument
        {
            Segments =
            [
                new TranscriptSegment
                {
                    Start = TimeSpan.FromSeconds(3),
                    End = TimeSpan.FromSeconds(7),
                    Speaker = "Speaker 1",
                    Text = "Plain line."
                }
            ]
        };

        var formatted = new TranscriptFormatter().Format(transcript, TranscriptOutputMode.PlainText);

        Assert.AreEqual("Plain line.", formatted);
    }
}
