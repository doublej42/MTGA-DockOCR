using System.Text;
using MTGADockOCR.Core.Models;

namespace MTGADockOCR.Core.Services;

public sealed class DeckListFormatter
{
    public string Format(IEnumerable<DeckCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var consolidatedCards = cards
            .Where(card => card.IsValid)
            .GroupBy(card => card.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new DeckCard(group.Sum(card => card.Quantity), group.First().Name.Trim()));

        var builder = new StringBuilder();
        foreach (var card in consolidatedCards)
        {
            builder.Append(card.Quantity);
            builder.Append(' ');
            builder.AppendLine(card.Name);
        }

        return builder.ToString();
    }
}