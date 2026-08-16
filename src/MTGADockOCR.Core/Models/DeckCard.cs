namespace MTGADockOCR.Core.Models;

public sealed record DeckCard(int Quantity, string Name)
{
    public bool IsValid => Quantity > 0 && !string.IsNullOrWhiteSpace(Name);
}