namespace App.Core.Live;

public static class OverlapTextDeduplicator
{
    public static string RemoveOverlap(string committedText, string candidateText, int maxWordsToCompare = 18)
    {
        if (string.IsNullOrWhiteSpace(candidateText))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(committedText))
        {
            return candidateText.Trim();
        }

        var committedWords = SplitWords(committedText).TakeLast(maxWordsToCompare).ToArray();
        var candidateWords = SplitWords(candidateText).ToArray();
        var committedNormalized = committedWords.Select(Normalize).Where(word => word.Length > 0).ToArray();
        var candidateNormalized = candidateWords.Select(Normalize).Where(word => word.Length > 0).ToArray();
        if (committedNormalized.Length == 0 || candidateNormalized.Length == 0)
        {
            return candidateText.Trim();
        }

        var fullCandidate = string.Join(" ", candidateNormalized);
        var committedTail = string.Join(" ", committedNormalized);
        if (committedTail.Contains(fullCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var overlap = Math.Min(committedNormalized.Length, candidateNormalized.Length);
        while (overlap > 0)
        {
            var suffix = committedNormalized.Skip(committedNormalized.Length - overlap);
            var prefix = candidateNormalized.Take(overlap);
            if (suffix.SequenceEqual(prefix, StringComparer.OrdinalIgnoreCase))
            {
                return string.Join(" ", candidateWords.Skip(overlap)).Trim();
            }

            overlap--;
        }

        return candidateText.Trim();
    }

    private static IEnumerable<string> SplitWords(string text)
    {
        return text
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => !string.IsNullOrWhiteSpace(word));
    }

    private static string Normalize(string word)
    {
        return word.Trim(' ', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']').ToLowerInvariant();
    }
}
