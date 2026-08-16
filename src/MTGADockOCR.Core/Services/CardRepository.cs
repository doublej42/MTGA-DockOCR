using Microsoft.Data.Sqlite;

namespace MTGADockOCR.Core.Services;

public sealed class CardRepository
{
    private readonly string _connectionString;

    public CardRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
    }

        public async Task<IReadOnlyList<CardDatabaseMatch>> FindExactMatchesAsync(string recognizedName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recognizedName);

        const string query = """
                        SELECT DISTINCT
                                name,
                                CASE
                                        WHEN faceName = $name THEN faceName
                                        WHEN name = $name OR asciiName = $name OR printedName = $name THEN name
                                        WHEN name LIKE $name || ' // %' THEN substr(name, 1, instr(name, ' // ') - 1)
                                        WHEN name LIKE '% // ' || $name THEN $name
                                END AS exportName
            FROM cards
            WHERE language = 'English'
                            AND (
                                    name = $name
                                    OR asciiName = $name
                                    OR printedName = $name
                                    OR faceName = $name
                                    OR name LIKE $name || ' // %'
                                    OR name LIKE '% // ' || $name
                            )
                        ORDER BY name, exportName
            LIMIT 10;
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$name", recognizedName.Trim());

        return await ReadMatchesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetCanonicalNamesAsync(CancellationToken cancellationToken)
    {
        const string query = """
            SELECT DISTINCT name
            FROM cards
            WHERE language = 'English' AND name IS NOT NULL AND name <> ''
            ORDER BY name;
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(query, connection);
        return await ReadNamesAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadNamesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<IReadOnlyList<CardDatabaseMatch>> ReadMatchesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var matches = new List<CardDatabaseMatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new CardDatabaseMatch(reader.GetString(0), reader.GetString(1)));
        }

        return matches;
    }
}

public sealed record CardDatabaseMatch(string DatabaseName, string ExportName);