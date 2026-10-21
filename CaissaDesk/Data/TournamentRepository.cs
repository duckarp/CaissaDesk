using System.Text.Json;
using Caissa.Shared.Models;
using Microsoft.Data.Sqlite;

namespace CaissaDesk.Data;

public class TournamentRepository : ITournamentRepository
{
    private readonly string _connectionString;
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public TournamentRepository(string? connectionString = null)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _connectionString = connectionString;
            return;
        }

        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaissaDesk");
        Directory.CreateDirectory(dataDir);
        string dbPath = Path.Combine(dataDir, "caissadesk.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            string createSql = """
                CREATE TABLE IF NOT EXISTS tournaments (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    system INTEGER NOT NULL,
                    time_control INTEGER NOT NULL,
                    total_rounds INTEGER NOT NULL,
                    player_count INTEGER NOT NULL,
                    fide_rated INTEGER NOT NULL,
                    start_date TEXT NOT NULL,
                    end_date TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    chief_arbiter TEXT,
                    venue TEXT,
                    is_finished INTEGER NOT NULL DEFAULT 0,
                    json_data TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_tournaments_updated ON tournaments(updated_at DESC);
            """;

            using var cmd = new SqliteCommand(createSql, conn);
            await cmd.ExecuteNonQueryAsync();

            // Migration for existing databases that were created before is_finished was added
            try
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE tournaments ADD COLUMN is_finished INTEGER NOT NULL DEFAULT 0;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Column already exists
            }

            try
            {
                using var idxCmd = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_tournaments_finished ON tournaments(is_finished, updated_at DESC);", conn);
                await idxCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Index already exists
            }

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<TournamentSummary>> GetAllSummariesAsync(bool? isFinished = null)
    {
        await InitializeAsync();
        var list = new List<TournamentSummary>();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = isFinished.HasValue
            ? """
                SELECT id, name, system, time_control, total_rounds, player_count, 
                       fide_rated, start_date, end_date, updated_at, chief_arbiter, venue, is_finished
                FROM tournaments
                WHERE is_finished = @is_finished
                ORDER BY updated_at DESC;
              """
            : """
                SELECT id, name, system, time_control, total_rounds, player_count, 
                       fide_rated, start_date, end_date, updated_at, chief_arbiter, venue, is_finished
                FROM tournaments
                ORDER BY updated_at DESC;
              """;

        using var cmd = new SqliteCommand(sql, conn);
        if (isFinished.HasValue)
        {
            cmd.Parameters.AddWithValue("@is_finished", isFinished.Value ? 1 : 0);
        }
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new TournamentSummary
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                System = (TournamentSystem)reader.GetInt32(2),
                TimeControl = (TimeControlCategory)reader.GetInt32(3),
                TotalRounds = reader.GetInt32(4),
                PlayerCount = reader.GetInt32(5),
                FideRated = reader.GetInt32(6) == 1,
                StartDate = DateTime.Parse(reader.GetString(7)),
                EndDate = DateTime.Parse(reader.GetString(8)),
                UpdatedAt = DateTime.Parse(reader.GetString(9)),
                ChiefArbiter = reader.IsDBNull(10) ? null : reader.GetString(10),
                Venue = reader.IsDBNull(11) ? null : reader.GetString(11),
                IsFinished = reader.GetInt32(12) == 1
            });
        }

        return list;
    }

    public async Task<Tournament?> GetByIdAsync(string id)
    {
        await InitializeAsync();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "SELECT json_data FROM tournaments WHERE id = @id LIMIT 1;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);

        var json = await cmd.ExecuteScalarAsync() as string;
        if (string.IsNullOrEmpty(json)) return null;

        return JsonSerializer.Deserialize<Tournament>(json, JsonOptions);
    }

    public async Task SaveAsync(Tournament tournament)
    {
        await InitializeAsync();

        tournament.UpdatedAt = DateTime.UtcNow;
        string json = JsonSerializer.Serialize(tournament, JsonOptions);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string upsertSql = """
            INSERT INTO tournaments (
                id, name, system, time_control, total_rounds, player_count,
                fide_rated, start_date, end_date, updated_at, chief_arbiter, venue, is_finished, json_data
            ) VALUES (
                @id, @name, @system, @time_control, @total_rounds, @player_count,
                @fide_rated, @start_date, @end_date, @updated_at, @chief_arbiter, @venue, @is_finished, @json_data
            )
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                system = excluded.system,
                time_control = excluded.time_control,
                total_rounds = excluded.total_rounds,
                player_count = excluded.player_count,
                fide_rated = excluded.fide_rated,
                start_date = excluded.start_date,
                end_date = excluded.end_date,
                updated_at = excluded.updated_at,
                chief_arbiter = excluded.chief_arbiter,
                venue = excluded.venue,
                is_finished = excluded.is_finished,
                json_data = excluded.json_data;
        """;

        using var cmd = new SqliteCommand(upsertSql, conn);
        cmd.Parameters.AddWithValue("@id", tournament.Id);
        cmd.Parameters.AddWithValue("@name", tournament.Name);
        cmd.Parameters.AddWithValue("@system", (int)tournament.System);
        cmd.Parameters.AddWithValue("@time_control", (int)tournament.TimeControl);
        cmd.Parameters.AddWithValue("@total_rounds", tournament.TotalRounds);
        cmd.Parameters.AddWithValue("@player_count", tournament.Players.Count);
        cmd.Parameters.AddWithValue("@fide_rated", tournament.FideRated ? 1 : 0);
        cmd.Parameters.AddWithValue("@start_date", tournament.StartDate.ToString("o"));
        cmd.Parameters.AddWithValue("@end_date", tournament.EndDate.ToString("o"));
        cmd.Parameters.AddWithValue("@updated_at", tournament.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@chief_arbiter", (object?)tournament.ChiefArbiter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@venue", (object?)tournament.Venue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_finished", tournament.IsFinished ? 1 : 0);
        cmd.Parameters.AddWithValue("@json_data", json);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetFinishedAsync(string id, bool isFinished)
    {
        var tournament = await GetByIdAsync(id);
        if (tournament == null) return;

        tournament.IsFinished = isFinished;
        tournament.FinishedAt = isFinished ? DateTime.UtcNow : null;
        await SaveAsync(tournament);
    }

    public async Task DeleteAsync(string id)
    {
        await InitializeAsync();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "DELETE FROM tournaments WHERE id = @id;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string> ExportJsonAsync(string id)
    {
        var tournament = await GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Tournament with id '{id}' not found.");

        return JsonSerializer.Serialize(tournament, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<Tournament> ImportJsonAsync(string jsonContent)
    {
        var tournament = JsonSerializer.Deserialize<Tournament>(jsonContent, JsonOptions)
            ?? throw new FormatException("Invalid tournament JSON structure.");

        // If ID collides or is empty, assign new ID
        if (string.IsNullOrWhiteSpace(tournament.Id))
        {
            tournament.Id = Guid.NewGuid().ToString("N");
        }

        await SaveAsync(tournament);
        return tournament;
    }

    public async Task<Tournament> CreateDemoTournamentAsync()
    {
        var demo = new Tournament
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Caissa Masters Invitational 2026",
            Organizer = "Caissa Chess Club",
            ChiefArbiter = "IA Tomas K.",
            Venue = "Grand Hotel Hall",
            Federation = "SVK",
            System = TournamentSystem.SwissDutch,
            TimeControl = TimeControlCategory.Rapid,
            TimeControlDetails = "15m + 10s",
            TotalRounds = 5,
            FideRated = true,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(2),
            Players =
            [
                new Player { StartRank = 1, FullName = "Carlsen, Magnus", Title = "GM", Federation = "NOR", FideRating = 2832, NationalRating = 2850, FideId = "1503014", BirthYear = 1990 },
                new Player { StartRank = 2, FullName = "Nakamura, Hikaru", Title = "GM", Federation = "USA", FideRating = 2802, NationalRating = 2820, FideId = "2016192", BirthYear = 1987 },
                new Player { StartRank = 3, FullName = "Caruana, Fabiano", Title = "GM", Federation = "USA", FideRating = 2795, NationalRating = 2810, FideId = "2020009", BirthYear = 1992 },
                new Player { StartRank = 4, FullName = "Firouzja, Alireza", Title = "GM", Federation = "FRA", FideRating = 2767, NationalRating = 2780, FideId = "12573981", BirthYear = 2003 },
                new Player { StartRank = 5, FullName = "Gukesh, D", Title = "GM", Federation = "IND", FideRating = 2766, NationalRating = 2770, FideId = "46616543", BirthYear = 2006 },
                new Player { StartRank = 6, FullName = "Erigaisi, Arjun", Title = "GM", Federation = "IND", FideRating = 2761, NationalRating = 2765, FideId = "35009192", BirthYear = 2003 },
                new Player { StartRank = 7, FullName = "Nepomniachtchi, Ian", Title = "GM", Federation = "FID", FideRating = 2758, NationalRating = 2760, FideId = "4152956", BirthYear = 1990 },
                new Player { StartRank = 8, FullName = "Praggnanandhaa, R", Title = "GM", Federation = "IND", FideRating = 2747, NationalRating = 2750, FideId = "25059530", BirthYear = 2005 },
                new Player { StartRank = 9, FullName = "So, Wesley", Title = "GM", Federation = "USA", FideRating = 2745, NationalRating = 2750, FideId = "5202213", BirthYear = 1993 },
                new Player { StartRank = 10, FullName = "Ding, Liren", Title = "GM", Federation = "CHN", FideRating = 2736, NationalRating = 2740, FideId = "8603677", BirthYear = 1992 }
            ]
        };

        await SaveAsync(demo);
        return demo;
    }
}
