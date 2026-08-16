namespace MTGADockOCR.Core.Models;

public enum CardMatchStatus
{
    Matched,
    Ambiguous,
    Unmatched,
}

public sealed record CardCandidate(string Name, double Score);

public sealed record CardMatch(
    string RecognizedName,
    CardMatchStatus Status,
    string? CanonicalName,
    IReadOnlyList<CardCandidate> Suggestions);