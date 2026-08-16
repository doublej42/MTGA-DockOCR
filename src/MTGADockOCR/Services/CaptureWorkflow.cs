using System.Drawing;
using System.Drawing.Imaging;
using MTGADockOCR.Core.Models;
using MTGADockOCR.Core.Services;

namespace MTGADockOCR.Services;

public sealed class CaptureWorkflow : IDisposable
{
    private readonly ForegroundWindowCaptureService _captureService;
    private readonly CardRepository _cardRepository;
    private readonly ISecretStore _secretStore;
    private readonly HttpClient _httpClient;
    private readonly Lock _gate = new();
    private byte[]? _firstCapture;
    private byte[]? _secondCapture;
    private byte[]? _combinedCapture;
    private bool _isAnalyzing;
    private bool _disposed;

    public CaptureWorkflow(ForegroundWindowCaptureService captureService, CardRepository cardRepository, ISecretStore secretStore, HttpClient httpClient)
    {
        _captureService = captureService;
        _cardRepository = cardRepository;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<string>? DeckReady;

    public event EventHandler<IReadOnlyList<RecognizedCardResult>>? RecognitionResultsReady;

    public event EventHandler<CaptureReview>? ReviewChanged;

    public event EventHandler<string>? DiagnosticLogged;

    public void RequestCapture()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var capture = _captureService.CapturePng();
            CaptureReview? review = null;
            lock (_gate)
            {
                if (_firstCapture is null)
                {
                    _firstCapture = capture;
                    _combinedCapture = capture;
                    Log($"Screenshot 1 captured ({capture.Length:N0} bytes).");
                    StatusChanged?.Invoke(this, "Screenshot 1 is ready to send. Press Ctrl+Alt+D again to add Screenshot 2, or select Send to Claude.");
                    review = CreateReview();
                }
                else if (_secondCapture is null)
                {
                    _secondCapture = capture;
                    _combinedCapture = ComposePng(_firstCapture, capture);
                    Log($"Screenshot 2 captured ({capture.Length:N0} bytes). Combined payload is {_combinedCapture.Length:N0} bytes.");
                    StatusChanged?.Invoke(this, "Both screenshots are ready. Review the combined image, then select Send to Claude.");
                    review = CreateReview();
                }
                else
                {
                    StatusChanged?.Invoke(this, "Two screenshots are already ready. Send them to Claude or clear the review before capturing again.");
                }
            }

            if (review is not null)
            {
                ReviewChanged?.Invoke(this, review);
            }
        }
        catch (Exception exception)
        {
            Log($"Capture failed: {exception}");
            StatusChanged?.Invoke(this, $"Capture failed: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _firstCapture = null;
            _secondCapture = null;
            _combinedCapture = null;
        }

        _httpClient.Dispose();
    }

    public void SendToClaude()
    {
        byte[]? imageToAnalyze;
        lock (_gate)
        {
            if (_isAnalyzing)
            {
                StatusChanged?.Invoke(this, "Claude analysis is already in progress.");
                return;
            }

            imageToAnalyze = _combinedCapture;
            if (imageToAnalyze is null)
            {
                StatusChanged?.Invoke(this, "Capture at least one screenshot before sending to Claude.");
                return;
            }

            _isAnalyzing = true;
        }

        Log("User selected Send to Claude.");
        StatusChanged?.Invoke(this, "Sending the reviewed screenshot to Claude.");
        _ = AnalyzeAsync(imageToAnalyze);
    }

    public void ClearCaptures()
    {
        lock (_gate)
        {
            if (_isAnalyzing)
            {
                StatusChanged?.Invoke(this, "Wait for the current Claude analysis to finish before clearing captures.");
                return;
            }

            _firstCapture = null;
            _secondCapture = null;
            _combinedCapture = null;
        }

        Log("Capture review cleared.");
        StatusChanged?.Invoke(this, "Capture review cleared. Press Ctrl+Alt+D to capture Screenshot 1.");
        ReviewChanged?.Invoke(this, new CaptureReview(null, null, null));
    }

    private async Task AnalyzeAsync(byte[] image)
    {
        try
        {
            Log($"Starting Claude analysis for a {image.Length:N0}-byte PNG.");
            var apiKey = await _secretStore.GetAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Log("Claude analysis skipped because no API key is configured.");
                StatusChanged?.Invoke(this, "Add your Claude API key in Settings before capturing a deck.");
                return;
            }

            Log($"Claude prompt:{Environment.NewLine}{ClaudeDeckRecognitionService.RecognitionPrompt}");
            var recognitionService = new ClaudeDeckRecognitionService(_httpClient, apiKey);
            recognitionService.RawResponseReceived += (_, response) => Log($"Claude raw response:{Environment.NewLine}{response}");
            var recognizedDeck = await recognitionService.RecognizeAsync(image, CancellationToken.None);
            var matchedCards = new List<DeckCard>();
            var recognitionResults = new List<RecognizedCardResult>();
            foreach (var card in recognizedDeck.Cards)
            {
                var exactMatches = await _cardRepository.FindExactMatchesAsync(card.Name, CancellationToken.None);
                if (exactMatches.Count == 1)
                {
                    matchedCards.Add(card with { Name = exactMatches[0].ExportName });
                    recognitionResults.Add(new RecognizedCardResult(card.Quantity, card.Name, exactMatches[0].DatabaseName, exactMatches[0].ExportName, true));
                }
                else
                {
                    recognitionResults.Add(new RecognizedCardResult(card.Quantity, card.Name, null, null, false));
                }
            }

            RecognitionResultsReady?.Invoke(this, recognitionResults);
            DeckReady?.Invoke(this, new DeckListFormatter().Format(matchedCards));
            var unmatchedCount = recognitionResults.Count(result => !result.IsMatched);
            Log($"Claude returned {recognitionResults.Count} card entries; {unmatchedCount} were unresolved in the database.");
            StatusChanged?.Invoke(this, unmatchedCount == 0
                ? $"Matched all {matchedCards.Count} returned card entries to the database."
                : $"Matched {matchedCards.Count} of {recognitionResults.Count} returned card entries. Unmatched entries are excluded from the deck list.");
        }
        catch (HttpRequestException exception)
        {
            Log($"Claude HTTP failure: {exception}");
            StatusChanged?.Invoke(this, $"Claude request failed: {exception.Message}");
        }
        catch (TaskCanceledException exception) when (!exception.CancellationToken.IsCancellationRequested)
        {
            Log($"Claude request timed out: {exception}");
            StatusChanged?.Invoke(this, "Claude did not respond within three minutes. Retry the request or capture a smaller deck list.");
        }
        catch (InvalidDataException exception)
        {
            Log($"Claude response validation failure: {exception}");
            StatusChanged?.Invoke(this, $"Claude returned an invalid deck response: {exception.Message}");
        }
        catch (Exception exception)
        {
            Log($"Deck analysis failure: {exception}");
            StatusChanged?.Invoke(this, $"Deck analysis failed: {exception.Message}");
        }
        finally
        {
            lock (_gate)
            {
                _isAnalyzing = false;
            }
        }
    }

    private static byte[] ComposePng(byte[] firstCapture, byte[] secondCapture)
    {
        using var firstStream = new MemoryStream(firstCapture);
        using var secondStream = new MemoryStream(secondCapture);
        using var firstImage = Image.FromStream(firstStream);
        using var secondImage = Image.FromStream(secondStream);
        var width = Math.Max(firstImage.Width, secondImage.Width);
        const int separatorHeight = 12;
        using var combinedImage = new Bitmap(width, firstImage.Height + separatorHeight + secondImage.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(combinedImage);
        graphics.Clear(Color.White);
        graphics.DrawImage(firstImage, 0, 0);
        graphics.FillRectangle(Brushes.Gray, 0, firstImage.Height, width, separatorHeight);
        graphics.DrawImage(secondImage, 0, firstImage.Height + separatorHeight);
        using var result = new MemoryStream();
        combinedImage.Save(result, ImageFormat.Png);
        return result.ToArray();
    }

    private CaptureReview CreateReview() => new(_firstCapture, _secondCapture, _combinedCapture);

    private void Log(string message)
    {
        DiagnosticLogged?.Invoke(this, $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}");
    }

}

public sealed record CaptureReview(byte[]? FirstScreenshot, byte[]? SecondScreenshot, byte[]? CombinedScreenshot);

public sealed record RecognizedCardResult(int Quantity, string ClaudeName, string? DatabaseName, string? ExportName, bool IsMatched);