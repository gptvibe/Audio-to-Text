using System.Text;
using App.Models.Domain;

namespace App.Core.Live;

public sealed class LiveTranscriptMerger
{
    private readonly List<TranscriptSegment> _segments = [];

    public IReadOnlyList<TranscriptSegment> Segments => _segments;

    public string PartialText { get; private set; } = string.Empty;

    public bool Apply(LiveTranscriptionEvent liveEvent)
    {
        return liveEvent.Kind switch
        {
            LiveTranscriptionEventKind.FinalSegment when liveEvent.Segment is not null => AddFinalSegment(liveEvent.Segment),
            LiveTranscriptionEventKind.PartialText => SetPartialText(liveEvent.PartialText ?? string.Empty),
            LiveTranscriptionEventKind.Stopped => SetPartialText(string.Empty),
            _ => false
        };
    }

    public bool AddFinalSegment(TranscriptSegment segment)
    {
        var text = segment.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (_segments.Count > 0)
        {
            var last = _segments[^1];
            if (IsDuplicate(last, segment, text))
            {
                return false;
            }

            var deduped = OverlapTextDeduplicator.RemoveOverlap(BuildCommittedText(), text);
            if (string.IsNullOrWhiteSpace(deduped))
            {
                return false;
            }

            text = deduped;
        }

        _segments.Add(segment with { Text = text });
        PartialText = OverlapTextDeduplicator.RemoveOverlap(BuildCommittedText(), PartialText);
        return true;
    }

    public bool SetPartialText(string text)
    {
        var deduped = OverlapTextDeduplicator.RemoveOverlap(BuildCommittedText(), text);
        if (string.Equals(PartialText, deduped, StringComparison.Ordinal))
        {
            return false;
        }

        PartialText = deduped;
        return true;
    }

    public TranscriptDocument ToDocument(string? sourceName, string? modelRepoId, string? language, TimeSpan? duration = null)
    {
        return new TranscriptDocument
        {
            SourceName = sourceName,
            ModelRepoId = modelRepoId,
            Language = language,
            Duration = duration,
            Segments = _segments.ToList(),
            SpeakerNames = _segments
                .Select(segment => segment.Speaker)
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(speaker => speaker!, speaker => speaker!, StringComparer.OrdinalIgnoreCase)
        };
    }

    public string BuildDisplayText(Func<TranscriptDocument, string> formatFinalSegments)
    {
        var formatted = formatFinalSegments(ToDocument(null, null, null));
        if (string.IsNullOrWhiteSpace(PartialText))
        {
            return formatted;
        }

        if (string.IsNullOrWhiteSpace(formatted))
        {
            return PartialText;
        }

        var builder = new StringBuilder(formatted.TrimEnd());
        builder.AppendLine();
        builder.Append(PartialText);
        return builder.ToString();
    }

    public void Clear()
    {
        _segments.Clear();
        PartialText = string.Empty;
    }

    private string BuildCommittedText()
    {
        return string.Join(" ", _segments.Select(segment => segment.Text));
    }

    private static bool IsDuplicate(TranscriptSegment last, TranscriptSegment candidate, string candidateText)
    {
        if (string.Equals(last.Text.Trim(), candidateText, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (last.End is not null && candidate.End is not null && candidate.End <= last.End.Value)
        {
            return true;
        }

        return false;
    }
}
