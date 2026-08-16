using MTGADockOCR.Core.Models;
using MTGADockOCR.Core.Services;
using Microsoft.Data.Sqlite;

namespace MTGADockOCR.Tests;

public sealed class UnitTest1
{
    [Fact]
    public void Match_NormalizesDiacriticsAndPunctuation()
    {
        var result = new CardNameMatcher().Match("El-Hajjaj", ["El-Hajjâj", "Lightning Bolt"]);

        Assert.Equal(CardMatchStatus.Matched, result.Status);
        Assert.Equal("El-Hajjâj", result.CanonicalName);
    }

    [Fact]
    public void Match_ReturnsRankedSuggestionsWhenNoExactMatchExists()
    {
        var result = new CardNameMatcher().Match("Lightnng Bolt", ["Llanowar Elves", "Lightning Bolt", "Lightning Helix"]);

        Assert.Equal(CardMatchStatus.Unmatched, result.Status);
        Assert.Equal("Lightning Bolt", result.Suggestions[0].Name);
    }

    [Fact]
    public void Format_ConsolidatesNamesWithoutChangingCanonicalCasing()
    {
        var deckList = new DeckListFormatter().Format([new DeckCard(2, "Lightning Bolt"), new DeckCard(1, "lightning bolt"), new DeckCard(4, "Opt")]);

        Assert.Equal("3 Lightning Bolt" + Environment.NewLine + "4 Opt" + Environment.NewLine, deckList);
    }

    [Fact]
    public void Format_PreservesTheFirstReturnedCardOrder()
    {
        var deckList = new DeckListFormatter().Format([new DeckCard(4, "Opt"), new DeckCard(2, "Lightning Bolt"), new DeckCard(1, "opt")]);

        Assert.Equal("5 Opt" + Environment.NewLine + "2 Lightning Bolt" + Environment.NewLine, deckList);
    }

    [Fact]
    public void CaptureSession_AnalyzesPairOnlyInsideThePairingWindow()
    {
        var time = DateTimeOffset.UtcNow;
        var coordinator = new CaptureSessionCoordinator(TimeSpan.FromSeconds(5));

        Assert.Equal(CaptureAction.WaitForPair, coordinator.RegisterCapture(time));
        Assert.Equal(CaptureAction.AnalyzePair, coordinator.RegisterCapture(time.AddSeconds(4.99)));
        Assert.Equal(CaptureAction.WaitForPair, coordinator.RegisterCapture(time.AddSeconds(6)));
        Assert.True(coordinator.TryExpire(time.AddSeconds(11)));
    }

    [Fact]
    public void ClaudeResponse_ParsesStructuredDeck()
    {
        const string response = """
            {"content":[{"type":"text","text":"{\"deckTitle\":\"Burn\",\"format\":\"Historic\",\"cards\":[{\"quantity\":4,\"name\":\"Lightning Bolt\"}]}"}]}
            """;

        var deck = ClaudeDeckRecognitionService.ParseResponse(response);

        Assert.Equal("Burn", deck.Title);
        Assert.Equal("Historic", deck.Format);
        Assert.Equal(new DeckCard(4, "Lightning Bolt"), Assert.Single(deck.Cards));
    }

    [Fact]
    public void ClaudeResponse_ParsesStructuredDeckInsideMarkdownFence()
    {
        const string response = """
            {"content":[{"type":"text","text":"```json\n{\"deckTitle\":\"Burn\",\"format\":\"Historic\",\"cards\":[{\"quantity\":4,\"name\":\"Lightning Bolt\"}]}\n```"}]}
            """;

        var deck = ClaudeDeckRecognitionService.ParseResponse(response);

        Assert.Equal("Burn", deck.Title);
        Assert.Equal("Historic", deck.Format);
        Assert.Equal(new DeckCard(4, "Lightning Bolt"), Assert.Single(deck.Cards));
    }

    [Fact]
    public void ClaudeResponse_ParsesStructuredDeckInsideUnclosedMarkdownFence()
    {
        const string response = """
            {"content":[{"type":"text","text":"```json\n{\"deckTitle\":\"Burn\",\"format\":\"Historic\",\"cards\":[{\"quantity\":4,\"name\":\"Lightning Bolt\"}]}"}]}
            """;

        var deck = ClaudeDeckRecognitionService.ParseResponse(response);

        Assert.Equal("Burn", deck.Title);
        Assert.Equal("Historic", deck.Format);
        Assert.Equal(new DeckCard(4, "Lightning Bolt"), Assert.Single(deck.Cards));
    }

    [Fact]
    public void ClaudeResponse_RejectsAResponseTruncatedAtTheOutputLimit()
    {
        const string response = """
            {"stop_reason":"max_tokens","content":[{"type":"text","text":"{\"cards\":["}]}
            """;

        var exception = Assert.Throws<InvalidDataException>(() => ClaudeDeckRecognitionService.ParseResponse(response));

        Assert.Contains("response limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DpapiSecretStore_RoundTripsASecretForTheCurrentUser()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"mtga-dock-ocr-{Guid.NewGuid():N}.bin");
        try
        {
            var store = new DpapiSecretStore(filePath);

            await store.SetAsync("test-api-key", CancellationToken.None);

            Assert.True(File.Exists(filePath));
            Assert.Equal("test-api-key", await store.GetAsync(CancellationToken.None));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task CardRepository_MatchesSplitCardFacesAndReturnsTheExportFace()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mtga-dock-ocr-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE cards (name TEXT, faceName TEXT, asciiName TEXT, printedName TEXT, language TEXT);
                    INSERT INTO cards VALUES ('Most Decrepit Old Bird // Speak Secrets', 'Most Decrepit Old Bird', NULL, NULL, 'English');
                    INSERT INTO cards VALUES ('Most Decrepit Old Bird // Speak Secrets', 'Speak Secrets', NULL, NULL, 'English');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var repository = new CardRepository(databasePath);
            var firstFace = await repository.FindExactMatchesAsync("Most Decrepit Old Bird", CancellationToken.None);
            var secondFace = await repository.FindExactMatchesAsync("Speak Secrets", CancellationToken.None);

            Assert.Equal(new CardDatabaseMatch("Most Decrepit Old Bird // Speak Secrets", "Most Decrepit Old Bird"), Assert.Single(firstFace));
            Assert.Equal(new CardDatabaseMatch("Most Decrepit Old Bird // Speak Secrets", "Speak Secrets"), Assert.Single(secondFace));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
