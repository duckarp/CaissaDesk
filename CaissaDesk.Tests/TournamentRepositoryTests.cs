using Caissa.Shared.Models;
using CaissaDesk.Data;
using CaissaDesk.Localization;

namespace CaissaDesk.Tests;

public class TournamentRepositoryTests
{
    private static (TournamentRepository repo, string dbPath) CreateTestRepo()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_caissadesk_{Guid.NewGuid():N}.db");
        var repo = new TournamentRepository($"Data Source={dbPath}");
        return (repo, dbPath);
    }

    [Fact]
    public async Task Repository_CreateAndRetrieve_ReturnsMatchingTournament()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            await repo.InitializeAsync();

            string tournamentId = Guid.NewGuid().ToString("N");
            var tournament = new Tournament
            {
                Id = tournamentId,
                Name = "Unit Test Championship",
                ChiefArbiter = "Arbiter Test",
                System = TournamentSystem.SwissDutch,
                TotalRounds = 5,
                Players =
                [
                    new Player { StartRank = 1, FullName = "Magnus Carlsen", FideRating = 2830, Title = "GM" },
                    new Player { StartRank = 2, FullName = "Hikaru Nakamura", FideRating = 2800, Title = "GM" }
                ]
            };

            await repo.SaveAsync(tournament);

            var retrieved = await repo.GetByIdAsync(tournamentId);
            Assert.NotNull(retrieved);
            Assert.Equal("Unit Test Championship", retrieved.Name);
            Assert.Equal(2, retrieved.Players.Count);
            Assert.Equal("Magnus Carlsen", retrieved.Players[0].FullName);

            var summaries = await repo.GetAllSummariesAsync();
            Assert.Contains(summaries, s => s.Id == tournamentId);

            // Test Export & Import
            string json = await repo.ExportJsonAsync(tournamentId);
            Assert.Contains("Unit Test Championship", json);

            // Modify player with advance bye requests and status
            tournament.Players[0].ByeRequests[3] = ByeType.HalfPoint;
            tournament.Players[1].Status = PlayerStatus.Withdrawn;
            tournament.Rounds.Add(new Round
            {
                RoundNumber = 1,
                Pairings =
                [
                    new Pairing { BoardNumber = 1, WhitePlayerId = tournament.Players[0].Id, BlackPlayerId = tournament.Players[1].Id, Result = MatchResult.WhiteWin }
                ]
            });
            await repo.SaveAsync(tournament);

            var reloaded = await repo.GetByIdAsync(tournamentId);
            Assert.NotNull(reloaded);
            Assert.Equal(ByeType.HalfPoint, reloaded.Players[0].ByeRequests[3]);
            Assert.Equal(PlayerStatus.Withdrawn, reloaded.Players[1].Status);
            Assert.Single(reloaded.Rounds);

            // Import
            var imported = await repo.ImportJsonAsync(json);
            Assert.NotNull(imported);
            Assert.Equal("Unit Test Championship", imported.Name);
            Assert.NotEqual(tournamentId, imported.Id); // existing tournament is never overwritten

            // Delete imported copy; original stays
            await repo.DeleteAsync(imported.Id);
            Assert.Null(await repo.GetByIdAsync(imported.Id));
            Assert.NotNull(await repo.GetByIdAsync(tournamentId));

            await repo.DeleteAsync(tournamentId);
            Assert.Null(await repo.GetByIdAsync(tournamentId));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Repository_TestAllExistingDatabaseTournaments()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            await repo.InitializeAsync();
            var t1 = new Tournament { Id = Guid.NewGuid().ToString("N"), Name = "Test Open 1" };
            var t2 = new Tournament { Id = Guid.NewGuid().ToString("N"), Name = "Test Open 2" };
            await repo.SaveAsync(t1);
            await repo.SaveAsync(t2);

            var all = await repo.GetAllSummariesAsync();
            Assert.NotEmpty(all);
            Assert.Equal(2, all.Count);
            foreach (var s in all)
            {
                var t = await repo.GetByIdAsync(s.Id);
                Assert.NotNull(t);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

#pragma warning disable BL0005
    [Fact]
    public async Task EditTournamentModal_LoadTournament_Works()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            await repo.InitializeAsync();
            var tour = new Tournament { Id = Guid.NewGuid().ToString("N"), Name = "Modal Test Tour" };
            await repo.SaveAsync(tour);

            var all = await repo.GetAllSummariesAsync();
            var first = all.First();

            var modal = new CaissaDesk.Components.Dashboard.EditTournamentModal();
            var repoProp = typeof(CaissaDesk.Components.Dashboard.EditTournamentModal).GetProperty("TournamentRepo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            repoProp!.SetValue(modal, repo);

            var loc = new LocalizationService(loadPersistedSettings: false);
            var locProp = typeof(CaissaDesk.Components.Dashboard.EditTournamentModal).GetProperty("Loc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            locProp!.SetValue(modal, loc);

            modal.TournamentId = first.Id;
            modal.IsOpen = true;

            var loadMethod = typeof(CaissaDesk.Components.Dashboard.EditTournamentModal).GetMethod("LoadTournamentAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var task = loadMethod!.Invoke(modal, new object[] { first.Id }) as Task;
            await task!;

            var tourField = typeof(CaissaDesk.Components.Dashboard.EditTournamentModal).GetField("_tournament", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var loaded = tourField!.GetValue(modal) as Tournament;
            Assert.NotNull(loaded);
            Assert.Equal(first.Id, loaded.Id);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task EditTournamentModal_WithDirectTournament_Works()
    {
        var tournament = new Tournament
        {
            Id = "test_trn_1",
            Name = "Direct Tournament Edit",
            ChiefArbiter = "Chief Arbiter",
            TotalRounds = 9
        };

        var modal = new CaissaDesk.Components.Dashboard.EditTournamentModal();
        modal.Tournament = tournament;
        modal.IsOpen = true;

        var setParamsMethod = typeof(CaissaDesk.Components.Dashboard.EditTournamentModal).GetMethod("OnParametersSetAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = setParamsMethod!.Invoke(modal, null) as Task;
        await task!;

        var tourField = typeof(CaissaDesk.Components.Dashboard.EditTournamentModal).GetField("_tournament", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var loaded = tourField!.GetValue(modal) as Tournament;
        Assert.NotNull(loaded);
        Assert.Equal("Direct Tournament Edit", loaded.Name);
    }

    [Fact]
    public async Task Repository_ArchiveAndRestore_FiltersCorrectly()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            await repo.InitializeAsync();

            string activeId = Guid.NewGuid().ToString("N");
            string archivedId = Guid.NewGuid().ToString("N");

            var activeTournament = new Tournament
            {
                Id = activeId,
                Name = "Active Open",
                System = TournamentSystem.SwissDutch,
                TotalRounds = 5,
                IsFinished = false
            };

            var archivedTournament = new Tournament
            {
                Id = archivedId,
                Name = "Archived Open",
                System = TournamentSystem.SwissDutch,
                TotalRounds = 5,
                IsFinished = true
            };

            await repo.SaveAsync(activeTournament);
            await repo.SaveAsync(archivedTournament);

            // Active list should only contain activeTournament
            var activeList = await repo.GetAllSummariesAsync(isFinished: false);
            Assert.Contains(activeList, s => s.Id == activeId);
            Assert.DoesNotContain(activeList, s => s.Id == archivedId);

            // Archived list should only contain archivedTournament
            var archivedList = await repo.GetAllSummariesAsync(isFinished: true);
            Assert.Contains(archivedList, s => s.Id == archivedId);
            Assert.DoesNotContain(archivedList, s => s.Id == activeId);

            // Finish the active tournament
            await repo.SetFinishedAsync(activeId, true);
            var activeAfterArchive = await repo.GetAllSummariesAsync(isFinished: false);
            var archivedAfterArchive = await repo.GetAllSummariesAsync(isFinished: true);
            Assert.DoesNotContain(activeAfterArchive, s => s.Id == activeId);
            Assert.Contains(archivedAfterArchive, s => s.Id == activeId);

            // Restore both
            await repo.SetFinishedAsync(activeId, false);
            await repo.SetFinishedAsync(archivedId, false);
            var finalActive = await repo.GetAllSummariesAsync(isFinished: false);
            Assert.Contains(finalActive, s => s.Id == activeId);
            Assert.Contains(finalActive, s => s.Id == archivedId);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UpdateExistingTournament_PersistsModifiedFields()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            await repo.InitializeAsync();

            string tournamentId = Guid.NewGuid().ToString("N");
            var tournament = new Tournament
            {
                Id = tournamentId,
                Name = "Original Open",
                Venue = "Original Venue",
                ChiefArbiter = "Original Arbiter",
                Federation = "SVK",
                TotalRounds = 5,
                TimeControl = TimeControlCategory.Rapid,
                TimeControlDetails = "15m + 5s"
            };

            await repo.SaveAsync(tournament);

            // Modify tournament details
            var loaded = await repo.GetByIdAsync(tournamentId);
            Assert.NotNull(loaded);

            loaded.Name = "Grand Masters Cup 2026";
            loaded.Venue = "Bratislava Castle";
            loaded.ChiefArbiter = "IA Tomas K.";
            loaded.DeputyArbiter = "FA John D.";
            loaded.Organizer = "Slovak Chess Union";
            loaded.Federation = "CZE";
            loaded.TotalRounds = 9;
            loaded.TimeControl = TimeControlCategory.Standard;
            loaded.TimeControlDetails = "90m + 30s";
            loaded.FideRated = true;

            await repo.SaveAsync(loaded);

            // Re-fetch and verify summary
            var summaries = await repo.GetAllSummariesAsync();
            var summary = summaries.FirstOrDefault(s => s.Id == tournamentId);
            Assert.NotNull(summary);
            Assert.Equal("Grand Masters Cup 2026", summary.Name);
            Assert.Equal("Bratislava Castle", summary.Venue);
            Assert.Equal("IA Tomas K.", summary.ChiefArbiter);
            Assert.Equal(9, summary.TotalRounds);
            Assert.Equal(TimeControlCategory.Standard, summary.TimeControl);
            Assert.True(summary.FideRated);

            // Re-fetch full tournament model
            var reloaded = await repo.GetByIdAsync(tournamentId);
            Assert.NotNull(reloaded);
            Assert.Equal("Grand Masters Cup 2026", reloaded.Name);
            Assert.Equal("Bratislava Castle", reloaded.Venue);
            Assert.Equal("IA Tomas K.", reloaded.ChiefArbiter);
            Assert.Equal("FA John D.", reloaded.DeputyArbiter);
            Assert.Equal("Slovak Chess Union", reloaded.Organizer);
            Assert.Equal("CZE", reloaded.Federation);
            Assert.Equal(9, reloaded.TotalRounds);
            Assert.Equal(TimeControlCategory.Standard, reloaded.TimeControl);
            Assert.Equal("90m + 30s", reloaded.TimeControlDetails);
            Assert.True(reloaded.FideRated);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private static void DeleteDbFiles(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public async Task ImportJson_WithExistingId_CreatesNewTournamentInsteadOfOverwriting()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            var original = new Tournament { Name = "Original", Players = [new Player { StartRank = 1, FullName = "A" }] };
            await repo.SaveAsync(original);
            string originalSyncKey = original.SyncKey;

            string json = await repo.ExportJsonAsync(original.Id);

            // Same tournament edited after export; importing the old file must not clobber it
            original.Name = "Original (edited)";
            await repo.SaveAsync(original);

            var imported = await repo.ImportJsonAsync(json);

            Assert.NotEqual(original.Id, imported.Id);
            Assert.False(string.IsNullOrWhiteSpace(imported.SyncKey));
            Assert.NotEqual(originalSyncKey, imported.SyncKey);

            var stillOriginal = await repo.GetByIdAsync(original.Id);
            Assert.NotNull(stillOriginal);
            Assert.Equal("Original (edited)", stillOriginal.Name);
            Assert.Equal(originalSyncKey, stillOriginal.SyncKey);

            var reloadedImport = await repo.GetByIdAsync(imported.Id);
            Assert.NotNull(reloadedImport);
            Assert.Equal("Original", reloadedImport.Name);
            Assert.Equal(imported.SyncKey, reloadedImport.SyncKey);

            Assert.Equal(2, (await repo.GetAllSummariesAsync()).Count);
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    [Fact]
    public async Task ImportJson_WithUnknownId_KeepsIdButRegeneratesSyncKey()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            var t = new Tournament { Name = "Foreign", SyncKey = "leaked-secret" };
            string json = System.Text.Json.JsonSerializer.Serialize(t);

            var imported = await repo.ImportJsonAsync(json);

            Assert.Equal(t.Id, imported.Id);
            Assert.NotEqual("leaked-secret", imported.SyncKey);
            Assert.False(string.IsNullOrWhiteSpace(imported.SyncKey));
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    [Fact]
    public async Task ExportJson_DoesNotContainSyncKey()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            var t = new Tournament { Name = "Secret Holder", SyncKey = "super-secret-sync-key" };
            await repo.SaveAsync(t);

            string json = await repo.ExportJsonAsync(t.Id);

            Assert.DoesNotContain("SyncKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("super-secret-sync-key", json);
            Assert.Contains("Secret Holder", json);

            // Local copy keeps its key for CaissaResults signing
            var local = await repo.GetByIdAsync(t.Id);
            Assert.Equal("super-secret-sync-key", local!.SyncKey);
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    [Fact]
    public async Task Initialize_EnablesWalJournalMode()
    {
        var (repo, dbPath) = CreateTestRepo();
        try
        {
            await repo.InitializeAsync();

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = new Microsoft.Data.Sqlite.SqliteCommand("PRAGMA journal_mode;", conn);
            var mode = (string?)await cmd.ExecuteScalarAsync();

            Assert.Equal("wal", mode, ignoreCase: true);
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }
}
