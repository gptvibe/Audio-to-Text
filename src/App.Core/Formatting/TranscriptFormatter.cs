using System.Text;
using App.Models.Domain;

namespace App.Core.Formatting;

public sealed class TranscriptFormatter
{
    public string Format(TranscriptDocument transcript, TranscriptOutputMode mode)
    {
        if (transcript.Segments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var segment in transcript.Segments)
        {
            var text = segment.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var prefix = BuildPrefix(segment, transcript.SpeakerNames, mode);
            builder.Append(prefix);
            builder.AppendLine(text);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildPrefix(
        TranscriptSegment segment,
        IReadOnlyDictionary<string, string> speakerNames,
        TranscriptOutputMode mode)
    {
        var includeTimestamp = mode is TranscriptOutputMode.Timestamps or TranscriptOutputMode.SpeakersAndTimestamps;
        var includeSpeaker = mode is TranscriptOutputMode.Speakers or TranscriptOutputMode.SpeakersAndTimestamps;
        var parts = new List<string>();

        if (includeTimestamp && segment.Start is not null)
        {
            var start = FormatTimestamp(segment.Start.Value);
            var end = segment.End is null ? null : FormatTimestamp(segment.End.Value);
            parts.Add(end is null ? $"[{start}]" : $"[{start} - {end}]");
        }

        if (includeSpeaker && !string.IsNullOrWhiteSpace(segment.Speaker))
        {
            var speaker = speakerNames.TryGetValue(segment.Speaker, out var name) ? name : segment.Speaker;
            parts.Add($"{speaker}:");
        }

        return parts.Count == 0 ? string.Empty : string.Join(" ", parts) + " ";
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }
}
