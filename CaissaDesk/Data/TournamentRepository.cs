using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Caissa.Shared.Models;
using Caissa.Shared.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaissaDesk.Data;

public class TournamentRepository : ITournamentRepository
{
    /// <summary>Latest schema version; stored in <c>PRAGMA user_version</c>.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Version of the .cdesk export envelope written by <see cref="ExportJsonAsync"/>.</summary>
    public const int CdeskFormatVersion = 1;

    /// <summary>Name of the top-level JSON property carrying the .cdesk format version.</summary>
    public const string FormatVersionPropertyName = "formatVersion";

    // Sanity limits for imported files.
    public const int MaxImportPlayers = 5000;
    public const int MaxImportRounds = 500;

    private readonly string _connectionString;
    private readonly ILogger<TournamentRepository> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _isInitialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true
    };

    private sealed record Migration(int Version, string Description, Func<SqliteConnection, SqliteTransaction, Task> ApplyAsync);

    private static readonly Migration[] Migrations =
    [
        new(1, "Create tournaments table", CreateTournamentsTableAsync),
        new(2, "Add is_finished column and index", AddIsFinishedColumnAsync),
    ];

    public TournamentRepository(string? connectionString = null, ILogger<TournamentRepository>? logger = null)
    {
        _logger = logger ?? NullLogger<TournamentRepository>.Instance;

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

            using var conn = await OpenConnectionAsync();

            // WAL is persistent per database file; busy_timeout is per connection (see OpenConnectionAsync)
            using (var walCmd = new SqliteCommand("PRAGMA journal_mode=WAL;", conn))
            {
                await walCmd.ExecuteNonQueryAsync();
            }

            await MigrateAsync(conn);

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqliteCommand("PRAGMA busy_timeout=5000;", conn);
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private async Task MigrateAsync(SqliteConnection conn)
    {
        int version = await GetUserVersionAsync(conn, null);

        if (version > CurrentSchemaVersion)
        {
            _logger.LogWarning(
                "Database schema version {DbVersion} is newer than supported version {AppVersion}; continuing without migrating.",
                version, CurrentSchemaVersion);
            return;
        }

        foreach (var migration in Migrations.Where(m => m.Version > version).OrderBy(m => m.Version))
        {
            using var tx = conn.BeginTransaction();
            try
            {
                await migration.ApplyAsync(conn, tx);
                await ExecuteAsync(conn, tx, $"PRAGMA user_version = {migration.Version.ToString(CultureInfo.InvariantCulture)};");
                tx.Commit();
                _logger.LogInformation("Applied database migration {Version}: {Description}", migration.Version, migration.Description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database migration {Version} ({Description}) failed; rolled back.", migration.Version, migration.Description);
                throw;
            }
        }
    }

    private static async Task CreateTournamentsTableAsync(SqliteConnection conn, SqliteTransaction tx)
    {
        // Baseline schema (pre-versioning databases already have this table; IF NOT EXISTS keeps it idempotent).
        await ExecuteAsync(conn, tx, """
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
                json_data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_tournaments_updated ON tournaments(updated_at DESC);
            """);
    }

    private static async Task AddIsFinishedColumnAsync(SqliteConnection conn, SqliteTransaction tx)
    {
        if (!await ColumnExistsAsync(conn, tx, "tournaments", "is_finished"))
        {
            await ExecuteAsync(conn, tx, "ALTER TABLE tournaments ADD COLUMN is_finished INTEGER NOT NULL DEFAULT 0;");
        }

        await ExecuteAsync(conn, tx, "CREATE INDEX IF NOT EXISTS idx_tournaments_finished ON tournaments(is_finished, updated_at DESC);");
    }

    internal static async Task<int> GetUserVersionAsync(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    internal static async Task<bool> ColumnExistsAsync(SqliteConnection conn, SqliteTransaction? tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // table_info cannot be parameterised as a PRAGMA argument; use the table-valued function form instead.
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column;";
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task ExecuteAsync(SqliteConnection conn, SqliteTransaction? tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static DateTime ParseStoredDate(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string FormatStoredDate(DateTime value) =>
        value.ToString("o", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<TournamentSummary>> GetAllSummariesAsync(bool? isFinished = null)
    {
        await InitializeAsync();
        var list = new List<TournamentSummary>();

        using var conn = await OpenConnectionAsync();

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
                StartDate = ParseStoredDate(reader.GetString(7)),
                EndDate = ParseStoredDate(reader.GetString(8)),
                UpdatedAt = ParseStoredDate(reader.GetString(9)),
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

        using var conn = await OpenConnectionAsync();
        return await ReadTournamentAsync(conn, null, id);
    }

    private static async Task<Tournament?> ReadTournamentAsync(SqliteConnection conn, SqliteTransaction? tx, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT json_data FROM tournaments WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);

        var json = await cmd.ExecuteScalarAsync() as string;
        if (string.IsNullOrEmpty(json)) return null;

        return JsonSerializer.Deserialize<Tournament>(json, JsonOptions);
    }

    public async Task SaveAsync(Tournament tournament)
    {
        await InitializeAsync();

        using var conn = await OpenConnectionAsync();
        await UpsertAsync(conn, null, tournament);
    }

    private static async Task UpsertAsync(SqliteConnection conn, SqliteTransaction? tx, Tournament tournament)
    {
        tournament.UpdatedAt = DateTime.UtcNow;
        string json = JsonSerializer.Serialize(tournament, JsonOptions);

        const string upsertSql = """
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

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = upsertSql;
        cmd.Parameters.AddWithValue("@id", tournament.Id);
        cmd.Parameters.AddWithValue("@name", tournament.Name);
        cmd.Parameters.AddWithValue("@system", (int)tournament.System);
        cmd.Parameters.AddWithValue("@time_control", (int)tournament.TimeControl);
        cmd.Parameters.AddWithValue("@total_rounds", tournament.TotalRounds);
        cmd.Parameters.AddWithValue("@player_count", tournament.Players.Count);
        cmd.Parameters.AddWithValue("@fide_rated", tournament.FideRated ? 1 : 0);
        cmd.Parameters.AddWithValue("@start_date", FormatStoredDate(tournament.StartDate));
        cmd.Parameters.AddWithValue("@end_date", FormatStoredDate(tournament.EndDate));
        cmd.Parameters.AddWithValue("@updated_at", FormatStoredDate(tournament.UpdatedAt));
        cmd.Parameters.AddWithValue("@chief_arbiter", (object?)tournament.ChiefArbiter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@venue", (object?)tournament.Venue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_finished", tournament.IsFinished ? 1 : 0);
        cmd.Parameters.AddWithValue("@json_data", json);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetFinishedAsync(string id, bool isFinished)
    {
        await InitializeAsync();

        using var conn = await OpenConnectionAsync();

        // Read-modify-write under one (immediate) transaction so concurrent saves cannot interleave.
        using var tx = conn.BeginTransaction();
        var tournament = await ReadTournamentAsync(conn, tx, id);
        if (tournament == null)
        {
            tx.Rollback();
            return;
        }

        tournament.IsFinished = isFinished;
        tournament.FinishedAt = isFinished ? DateTime.UtcNow : null;
        await UpsertAsync(conn, tx, tournament);
        tx.Commit();
    }

    public async Task DeleteAsync(string id)
    {
        await InitializeAsync();

        using var conn = await OpenConnectionAsync();

        string sql = "DELETE FROM tournaments WHERE id = @id;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string> ExportJsonAsync(string id)
    {
        var tournament = await GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Tournament with id '{id}' not found.");

        // The envelope is the tournament object itself plus a leading "formatVersion" property.
        // Older readers ignore the unknown property, newer readers use it to detect the layout.
        var node = JsonSerializer.SerializeToNode(tournament, ExportJsonOptions)!.AsObject();
        // SyncKey is the per-tournament secret used to sign CaissaResults requests; it must never leave this machine.
        node.Remove(nameof(Tournament.SyncKey));
        node.Insert(0, FormatVersionPropertyName, CdeskFormatVersion);
        return node.ToJsonString(ExportJsonOptions);
    }

    public async Task<Tournament> ImportJsonAsync(string jsonContent)
    {
        var tournament = ParseImport(jsonContent);

        // Never overwrite an existing tournament: assign a new ID if empty or already present
        if (string.IsNullOrWhiteSpace(tournament.Id) || await ExistsAsync(tournament.Id))
        {
            tournament.Id = Guid.NewGuid().ToString("N");
        }

        // Imported files must not carry (or share) a sync secret
        tournament.SyncKey = CaissaSecurity.GenerateSecureKey(16);

        await SaveAsync(tournament);
        _logger.LogInformation("Imported tournament {TournamentId} ({Name}) with {PlayerCount} players and {RoundCount} rounds.",
            tournament.Id, tournament.Name, tournament.Players.Count, tournament.Rounds.Count);
        return tournament;
    }

    private async Task<bool> ExistsAsync(string id)
    {
        await InitializeAsync();

        using var conn = await OpenConnectionAsync();
        using var cmd = new SqliteCommand("SELECT 1 FROM tournaments WHERE id = @id LIMIT 1;", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Parses and validates the content of a .cdesk file. Accepts both the versioned envelope
    /// (with <c>formatVersion</c>) and legacy files without it. Throws <see cref="FormatException"/>
    /// with a human-readable message on any structural problem.
    /// </summary>
    public static Tournament ParseImport(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            throw new FormatException("The file is empty.");
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(jsonContent) as JsonObject
                ?? throw new FormatException("The file does not contain a tournament object.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The file is not valid JSON: {ex.Message}", ex);
        }

        string? versionKey = root.Select(kv => kv.Key)
            .FirstOrDefault(k => string.Equals(k, FormatVersionPropertyName, StringComparison.OrdinalIgnoreCase));
        if (versionKey != null)
        {
            if (root[versionKey] is not JsonValue versionNode || !versionNode.TryGetValue(out int version))
            {
                throw new FormatException("The file has an invalid formatVersion value.");
            }

            if (version < 1)
            {
                throw new FormatException($"The file has an invalid formatVersion ({version}).");
            }
            if (version > CdeskFormatVersion)
            {
                throw new FormatException(
                    $"The file was created by a newer version of CaissaDesk (format {version}; this version supports up to {CdeskFormatVersion}).");
            }

            root.Remove(versionKey);
        }

        Tournament? tournament;
        try
        {
            tournament = root.Deserialize<Tournament>(JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The file does not match the tournament format: {ex.Message}", ex);
        }

        if (tournament == null)
        {
            throw new FormatException("Invalid tournament JSON structure.");
        }

        ValidateImportedTournament(tournament);
        return tournament;
    }

    /// <summary>Validates the structural integrity of an imported tournament. Throws <see cref="FormatException"/>.</summary>
    public static void ValidateImportedTournament(Tournament tournament)
    {
        ArgumentNullException.ThrowIfNull(tournament);

        if (string.IsNullOrWhiteSpace(tournament.Name))
            throw new FormatException("The tournament has no name.");
        if (tournament.Players == null)
            throw new FormatException("The tournament has no player list.");
        if (tournament.Rounds == null)
            throw new FormatException("The tournament has no round list.");
        if (tournament.Players.Count > MaxImportPlayers)
            throw new FormatException($"The tournament has too many players ({tournament.Players.Count}; maximum is {MaxImportPlayers}).");
        if (tournament.Rounds.Count > MaxImportRounds)
            throw new FormatException($"The tournament has too many rounds ({tournament.Rounds.Count}; maximum is {MaxImportRounds}).");
        if (tournament.TotalRounds < 0 || tournament.TotalRounds > MaxImportRounds)
            throw new FormatException($"The tournament has an invalid total round count ({tournament.TotalRounds}).");

        tournament.TiebreakOrder ??= [];

        var playerIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < tournament.Players.Count; i++)
        {
            var player = tournament.Players[i]
                ?? throw new FormatException($"Player entry #{i + 1} is empty.");
            if (string.IsNullOrWhiteSpace(player.Id))
                throw new FormatException($"Player #{i + 1} has no ID.");
            if (!playerIds.Add(player.Id))
                throw new FormatException($"Duplicate player ID '{player.Id}'.");
            player.ByeRequests ??= [];
        }

        var roundNumbers = new HashSet<int>();
        foreach (var round in tournament.Rounds)
        {
            if (round == null)
                throw new FormatException("The tournament contains an empty round entry.");
            if (round.RoundNumber <= 0)
                throw new FormatException($"Invalid round number {round.RoundNumber}; round numbers must be positive.");
            if (round.RoundNumber > MaxImportRounds)
                throw new FormatException($"Invalid round number {round.RoundNumber}.");
            if (!roundNumbers.Add(round.RoundNumber))
                throw new FormatException($"Round {round.RoundNumber} appears more than once.");
            if (round.Pairings == null)
                throw new FormatException($"Round {round.RoundNumber} has no pairing list.");
            if (round.Pairings.Count > Math.Max(1, tournament.Players.Count))
                throw new FormatException($"Round {round.RoundNumber} has more pairings ({round.Pairings.Count}) than there are players.");

            foreach (var pairing in round.Pairings)
            {
                if (pairing == null)
                    throw new FormatException($"Round {round.RoundNumber} contains an empty pairing entry.");

                bool hasWhite = !string.IsNullOrEmpty(pairing.WhitePlayerId);
                bool hasBlack = !string.IsNullOrEmpty(pairing.BlackPlayerId);
                if (!hasWhite && !hasBlack)
                    throw new FormatException($"Round {round.RoundNumber}, board {pairing.BoardNumber}: pairing has no players.");
                if (hasWhite && !playerIds.Contains(pairing.WhitePlayerId!))
                    throw new FormatException($"Round {round.RoundNumber}, board {pairing.BoardNumber}: unknown white player ID '{pairing.WhitePlayerId}'.");
                if (hasBlack && !playerIds.Contains(pairing.BlackPlayerId!))
                    throw new FormatException($"Round {round.RoundNumber}, board {pairing.BoardNumber}: unknown black player ID '{pairing.BlackPlayerId}'.");
            }
        }
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
