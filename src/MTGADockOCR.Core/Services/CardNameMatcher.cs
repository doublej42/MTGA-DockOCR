using MTGADockOCR.Core.Models;

namespace MTGADockOCR.Core.Services;

public sealed class CardNameMatcher
{
    public CardMatch Match(string recognizedName, IEnumerable<string> candidateNames, int maximumSuggestions = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recognizedName);
        ArgumentNullException.ThrowIfNull(candidateNames);

        var normalizedName = CardNameNormalizer.Normalize(recognizedName);
        var distinctCandidates = candidateNames
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new Candidate(candidate, CardNameNormalizer.Normalize(candidate)))
            .ToList();

        var exactMatches = distinctCandidates
            .Where(candidate => candidate.NormalizedName == normalizedName)
            .Select(candidate => candidate.Name)
            .ToList();

        if (exactMatches.Count == 1)
        {
            return new CardMatch(recognizedName, CardMatchStatus.Matched, exactMatches[0], []);
        }

        if (exactMatches.Count > 1)
        {
            return new CardMatch(
                recognizedName,
                CardMatchStatus.Ambiguous,
                null,
                exactMatches.Select(name => new CardCandidate(name, 1d)).ToList());
        }

        var suggestions = distinctCandidates
            .Select(candidate => new CardCandidate(candidate.Name, Similarity(normalizedName, candidate.NormalizedName)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maximumSuggestions)
            .ToList();

        return new CardMatch(recognizedName, CardMatchStatus.Unmatched, null, suggestions);
    }

    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0d;
        }

        var matrix = new int[left.Length + 1, right.Length + 1];
        for (var index = 0; index <= left.Length; index++)
        {
            matrix[index, 0] = index;
        }

        for (var index = 0; index <= right.Length; index++)
        {
            matrix[0, index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                matrix[leftIndex, rightIndex] = Math.Min(
                    Math.Min(matrix[leftIndex - 1, rightIndex] + 1, matrix[leftIndex, rightIndex - 1] + 1),
                    matrix[leftIndex - 1, rightIndex - 1] + substitutionCost);
            }
        }

        var distance = matrix[left.Length, right.Length];
        return 1d - ((double)distance / Math.Max(left.Length, right.Length));
    }

    private sealed record Candidate(string Name, string NormalizedName);
}