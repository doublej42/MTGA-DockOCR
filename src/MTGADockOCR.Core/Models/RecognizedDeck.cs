namespace MTGADockOCR.Core.Models;

public sealed record RecognizedDeck(
    string? Title,
    string? Format,
    IReadOnlyList<DeckCard> Cards);