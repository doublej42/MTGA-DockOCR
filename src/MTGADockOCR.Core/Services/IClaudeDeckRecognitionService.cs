using MTGADockOCR.Core.Models;

namespace MTGADockOCR.Core.Services;

public interface IClaudeDeckRecognitionService
{
    Task<RecognizedDeck> RecognizeAsync(ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken);
}