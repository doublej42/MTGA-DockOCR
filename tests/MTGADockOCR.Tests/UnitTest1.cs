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
                    INSERT INTO cards VALUES ('Rampant Growth', NULL, NULL, NULL, 'English');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var repository = new CardRepository(databasePath);
            var firstFace = await repository.FindExactMatchesAsync("Most Decrepit Old Bird", CancellationToken.None);
            var secondFace = await repository.FindExactMatchesAsync("Speak Secrets", CancellationToken.None);
            var normalizedWhitespace = await repository.FindExactMatchesAsync("Rampant\u00A0\u200BGrowth", CancellationToken.None);

            Assert.Equal(new CardDatabaseMatch("Most Decrepit Old Bird // Speak Secrets", "Most Decrepit Old Bird"), Assert.Single(firstFace));
            Assert.Equal(new CardDatabaseMatch("Most Decrepit Old Bird // Speak Secrets", "Speak Secrets"), Assert.Single(secondFace));
            Assert.Equal(new CardDatabaseMatch("Rampant Growth", "Rampant Growth"), Assert.Single(normalizedWhitespace));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task CardRepository_MatchesEveryCardFromTheReportedFoundationDeck()
    {
        var databasePath = FindWorkspaceFile("AllPrintings.sqlite");
        var repository = new CardRepository(databasePath);
        string[] cardNames =
        [
            "Thranduil, the Elvenking", "Most Decrepit Old Bird", "Birds of Paradise", "Elvish Mystic",
            "Legolas's Quick Reflexes", "Deathrite Shaman", "Elvish Elegy", "Arcane Signet", "Bloom Tender",
            "Elvish Warmaster", "Fauna Shaman", "Guardian of the Halls", "Heroic Intervention", "Rampant Growth",
            "Wildborn Preserver", "Woodland Weavemaster", "Leaf-Crowned Visionary", "Perennial Gravewarden",
            "Elrond, Moon-Reader", "Roll-Roll-Roll-Roll", "Toxic Deluge", "Down in the Valley", "Mirkwood Pathmaker",
            "Necklace of Girion", "Reclamation Sage", "Shower of Arrows", "Springbloom Druid", "Tireless Provisioner",
            "Unforgiving Aim", "Wood Elves", "Arwen, Weaver of Hope", "Elvish Archdruid", "Shaman of the Pack",
            "Galadriel of Lothlórien", "Arwen's Gift", "Uncover the Moon-Letters", "Bitter Downfall", "Dawnhand Eulogist",
            "Celeborn the Wise", "Champions of the Perfect", "Elven Chorus", "Beast Whisperer", "Moon-Vigil Adherents",
            "Silvan Reveler", "Thranduil's Company", "Door of Destinies", "Lórien Revealed", "Gloom Ripper", "Live or Die",
            "Voice of the Woods", "Deathbloom Ritualist", "Banner of Kinship", "Vanquisher's Banner", "Bilbo's Burglaring",
            "Harmonized Crescendo", "Thranduil's Decree", "Haunting Voyage", "Cantankerous Keepers", "Chronicle of Victory",
            "Island", "Rivendell", "Swamp", "Forest", "Drowned Catacomb", "Shipwreck Marsh", "Undercity Sewers",
            "Underground River", "Watery Grave", "Deathcap Glade", "Mirkwood", "Overgrown Tomb", "Underground Mortuary",
            "Woodland Cemetery", "Dreamroot Cascade", "Elvenking's Halls", "Hedge Maze", "Hinterland Harbor", "Command Tower",
            "Echoing Cavern", "Elven Passage", "Reflecting Pool",
        ];

        var unresolvedNames = new List<string>();
        foreach (var cardName in cardNames)
        {
            var matches = await repository.FindExactMatchesAsync(cardName, CancellationToken.None);
            if (matches.Count != 1)
            {
                unresolvedNames.Add(cardName);
            }
        }

        Assert.True(unresolvedNames.Count == 0, $"Unresolved cards: {string.Join(", ", unresolvedNames)}");
    }

    private static string FindWorkspaceFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidatePath = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new FileNotFoundException($"Could not find {fileName} from the test output directory.");
    }
}
