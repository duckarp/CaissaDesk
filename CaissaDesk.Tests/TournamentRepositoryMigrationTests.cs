using System.Globalization;
using Caissa.Shared.Models;
using CaissaDesk.Data;
using Microsoft.Data.Sqlite;

namespace CaissaDesk.Tests;

public class TournamentRepositoryMigrationTests
{
    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"test_caissadesk_migr_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }

    private static async Task<long> ScalarAsync(string dbPath, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task FreshDatabase_IsCreatedAtCurrentSchemaVersion()
    {
        string dbPath = NewDbPath();
        try
        {
            var repo = new TournamentRepository($"Data Source={dbPath}");
            await repo.InitializeAsync();

            Assert.Equal(TournamentRepository.CurrentSchemaVersion, await ScalarAsync(dbPath, "PRAGMA user_version;"));
            Assert.Equal(1, await ScalarAsync(dbPath, "SELECT COUNT(*) FROM pragma_table_info('tournaments') WHERE name = 'is_finished';"));
            Assert.Equal(1, await ScalarAsync(dbPath, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_tournaments_finished';"));
            Assert.Equal(1, await ScalarAsync(dbPath, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_tournaments_updated';"));

            // A second repository on the same file re-runs initialisation without error.
            var repo2 = new TournamentRepository($"Data Source={dbPath}");
            await repo2.InitializeAsync();
            await repo2.SaveAsync(new Tournament { Name = "After reopen" });
            Assert.Single(await repo2.GetAllSummariesAsync());
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task LegacyDatabase_WithoutIsFinishedColumn_IsUpgradedAndKeepsData()
    {
        string dbPath = NewDbPath();
        try
        {
            // Schema as created by the very first releases: no is_finished column, user_version 0.
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE tournaments (
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
                    INSERT INTO tournaments VALUES (
                        'legacy1', 'Legacy Open', 0, 1, 7, 0, 1,
                        '2024-05-01T00:00:00.0000000', '2024-05-03T00:00:00.0000000', '2024-05-03T12:30:00.0000000Z',
                        'IA Old', 'Old Hall', '{"Id":"legacy1","Name":"Legacy Open","TotalRounds":7}'
                    );
                    """;
                await cmd.ExecuteNonQueryAsync();
            }
            Assert.Equal(0, await ScalarAsync(dbPath, "PRAGMA user_version;"));

            var repo = new TournamentRepository($"Data Source={dbPath}");
            await repo.InitializeAsync();

            Assert.Equal(TournamentRepository.CurrentSchemaVersion, await ScalarAsync(dbPath, "PRAGMA user_version;"));
            Assert.Equal(1, await ScalarAsync(dbPath, "SELECT COUNT(*) FROM pragma_table_info('tournaments') WHERE name = 'is_finished';"));

            var summaries = await repo.GetAllSummariesAsync();
            var legacy = Assert.Single(summaries);
            Assert.Equal("Legacy Open", legacy.Name);
            Assert.False(legacy.IsFinished);
            Assert.Equal(new DateTime(2024, 5, 1), legacy.StartDate);
            Assert.Equal(DateTimeKind.Utc, legacy.UpdatedAt.Kind);
            Assert.Equal(new DateTime(2024, 5, 3, 12, 30, 0, DateTimeKind.Utc), legacy.UpdatedAt);

            Assert.Single(await repo.GetAllSummariesAsync(isFinished: false));
            await repo.SetFinishedAsync("legacy1", true);
            Assert.Single(await repo.GetAllSummariesAsync(isFinished: true));
            var reloaded = await repo.GetByIdAsync("legacy1");
            Assert.NotNull(reloaded);
            Assert.True(reloaded.IsFinished);
            Assert.NotNull(reloaded.FinishedAt);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task UnversionedDatabase_ThatAlreadyHasIsFinished_IsStampedWithoutError()
    {
        string dbPath = NewDbPath();
        try
        {
            // Databases created by the previous (try/catch ALTER TABLE) code: column exists, user_version 0.
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE tournaments (
                        id TEXT PRIMARY KEY, name TEXT NOT NULL, system INTEGER NOT NULL, time_control INTEGER NOT NULL,
                        total_rounds INTEGER NOT NULL, player_count INTEGER NOT NULL, fide_rated INTEGER NOT NULL,
                        start_date TEXT NOT NULL, end_date TEXT NOT NULL, updated_at TEXT NOT NULL,
                        chief_arbiter TEXT, venue TEXT, is_finished INTEGER NOT NULL DEFAULT 0, json_data TEXT NOT NULL
                    );
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var repo = new TournamentRepository($"Data Source={dbPath}");
            await repo.InitializeAsync();
            Assert.Equal(TournamentRepository.CurrentSchemaVersion, await ScalarAsync(dbPath, "PRAGMA user_version;"));
            Assert.Equal(1, await ScalarAsync(dbPath, "SELECT COUNT(*) FROM pragma_table_info('tournaments') WHERE name = 'is_finished';"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task FailingMigration_IsRolledBackAndSurfaced()
    {
        string dbPath = NewDbPath();
        try
        {
            // A conflicting object named like the index migration 2 creates makes the migration fail for real.
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE tournaments (
                        id TEXT PRIMARY KEY, name TEXT NOT NULL, system INTEGER NOT NULL, time_control INTEGER NOT NULL,
                        total_rounds INTEGER NOT NULL, player_count INTEGER NOT NULL, fide_rated INTEGER NOT NULL,
                        start_date TEXT NOT NULL, end_date TEXT NOT NULL, updated_at TEXT NOT NULL,
                        chief_arbiter TEXT, venue TEXT, json_data TEXT NOT NULL
                    );
                    CREATE TABLE idx_tournaments_finished (x INTEGER);
                    PRAGMA user_version = 1;
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var repo = new TournamentRepository($"Data Source={dbPath}");
            await Assert.ThrowsAsync<SqliteException>(repo.InitializeAsync);

            // Rolled back: version not bumped and the ALTER TABLE from the same step was undone.
            Assert.Equal(1, await ScalarAsync(dbPath, "PRAGMA user_version;"));
            Assert.Equal(0, await ScalarAsync(dbPath, "SELECT COUNT(*) FROM pragma_table_info('tournaments') WHERE name = 'is_finished';"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Summaries_ParseDatesIndependentlyOfCurrentCulture()
    {
        string dbPath = NewDbPath();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var repo = new TournamentRepository($"Data Source={dbPath}");
            var t = new Tournament { Name = "Culture Test", StartDate = new DateTime(2026, 3, 12), EndDate = new DateTime(2026, 3, 14) };
            await repo.SaveAsync(t);

            CultureInfo.CurrentCulture = new CultureInfo("ar-SA"); // non-Gregorian default calendar
            var summary = Assert.Single(await repo.GetAllSummariesAsync());
            CultureInfo.CurrentCulture = originalCulture;

            Assert.Equal(new DateTime(2026, 3, 12), summary.StartDate);
            Assert.Equal(new DateTime(2026, 3, 14), summary.EndDate);
            Assert.Equal(DateTimeKind.Utc, summary.UpdatedAt.Kind);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ConcurrentSetFinished_AndInitialize_AreSafe()
    {
        string dbPath = NewDbPath();
        try
        {
            var repo = new TournamentRepository($"Data Source={dbPath}");
            var ids = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                var t = new Tournament { Name = $"T{i}" };
                ids.Add(t.Id);
                await repo.SaveAsync(t);
            }

            var fresh = new TournamentRepository($"Data Source={dbPath}");
            await Task.WhenAll(ids.Select(id => Task.Run(() => fresh.SetFinishedAsync(id, true))));

            Assert.Equal(5, (await fresh.GetAllSummariesAsync(isFinished: true)).Count);
            await fresh.SetFinishedAsync("does-not-exist", true); // no-op, no throw
        }
        finally
        {
            Cleanup(dbPath);
        }
    }
}
