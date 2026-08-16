using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MTGADockOCR.Core.Models;

namespace MTGADockOCR.Core.Services;

public sealed class ClaudeDeckRecognitionService : IClaudeDeckRecognitionService
{
    private const string Prompt = """
        Read this Magic: The Gathering deck screenshot. Return only valid JSON with this exact shape:
        {"deckTitle":"optional string or null","format":"optional string or null","cards":[{"quantity":positive integer,"name":"card name"}]}.
        Extract only visible, readable card entries. Consolidate duplicate card names. Do not guess, autocomplete, or invent a card name. Omit an entry when its name cannot be read with confidence.
        """;

    public static string RecognitionPrompt => Prompt;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public event EventHandler<string>? RawResponseReceived;

    public ClaudeDeckRecognitionService(HttpClient httpClient, string apiKey, string model = "claude-sonnet-5")
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<RecognizedDeck> RecognizeAsync(ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken)
    {
        if (imageBytes.IsEmpty)
        {
            throw new ArgumentException("An image is required for recognition.", nameof(imageBytes));
        }

        var requestBody = new
        {
            model = _model,
            max_tokens = 40960,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = "image/png", data = Convert.ToBase64String(imageBytes.Span) } },
                        new { type = "text", text = Prompt },
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        RawResponseReceived?.Invoke(this, responseBody);
        response.EnsureSuccessStatusCode();

        return ParseResponse(responseBody);
    }

    public static RecognizedDeck ParseResponse(string responseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBody);

        using var responseDocument = JsonDocument.Parse(responseBody);
        if (responseDocument.RootElement.TryGetProperty("stop_reason", out var stopReason)
            && string.Equals(stopReason.GetString(), "max_tokens", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Claude reached its response limit before it finished the deck list. Capture a smaller list or retry the request.");
        }

        var text = responseDocument.RootElement
            .GetProperty("content")
            .EnumerateArray()
            .Where(content => content.GetProperty("type").GetString() == "text")
            .Select(content => content.GetProperty("text").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new InvalidDataException("Claude returned no text content.");

        var deck = JsonSerializer.Deserialize<ClaudeDeckResponse>(ExtractJsonObject(text), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })
            ?? throw new InvalidDataException("Claude returned an empty deck response.");
        if (deck.Cards is null || deck.Cards.Count == 0 || deck.Cards.Any(card => card.Quantity <= 0 || string.IsNullOrWhiteSpace(card.Name)))
        {
            throw new InvalidDataException("Claude returned invalid deck card entries.");
        }

        return new RecognizedDeck(
            deck.DeckTitle,
            deck.Format,
            deck.Cards.Select(card => new DeckCard(card.Quantity, card.Name.Trim())).ToList());
    }

    private static string ExtractJsonObject(string text)
    {
        var trimmedText = text.Trim();
        if (trimmedText.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmedText.IndexOf('\n');
            var closingFence = trimmedText.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineBreak < 0)
            {
                throw new InvalidDataException("Claude returned an incomplete JSON code fence.");
            }

            trimmedText = closingFence > firstLineBreak
                ? trimmedText[(firstLineBreak + 1)..closingFence].Trim()
                : trimmedText[(firstLineBreak + 1)..].Trim();
        }

        var firstBrace = trimmedText.IndexOf('{');
        var lastBrace = trimmedText.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            throw new InvalidDataException("Claude returned no JSON object.");
        }

        return trimmedText[firstBrace..(lastBrace + 1)];
    }

    private sealed record ClaudeDeckResponse(string? DeckTitle, string? Format, List<ClaudeDeckCard>? Cards);

    private sealed record ClaudeDeckCard(int Quantity, string Name);
}