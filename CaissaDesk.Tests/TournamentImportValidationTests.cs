using System.Text.Json;
using System.Text.Json.Nodes;
using Caissa.Shared.Models;
using CaissaDesk.Data;
using Microsoft.Data.Sqlite;

namespace CaissaDesk.Tests;

public class TournamentImportValidationTests
{
    private static Tournament SampleTournament()
    {
        var p1 = new Player { StartRank = 1, FullName = "Alpha, Ann" };
        var p2 = new Player { StartRank = 2, FullName = "Beta, Bob" };
        var p3 = new Player { StartRank = 3, FullName = "Gamma, Gil" };
        return new Tournament
        {
            Name = "Validation Open",
            TotalRounds = 3,
            Players = [p1, p2, p3],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing { BoardNumber = 1, WhitePlayerId = p1.Id, BlackPlayerId = p2.Id, Result = MatchResult.WhiteWin },
                        new Pairing { BoardNumber = 2, WhitePlayerId = p3.Id, BlackPlayerId = null }
                    ]
                }
            ]
        };
    }

    private static JsonObject ToNode(Tournament t) => JsonSerializer.SerializeToNode(t)!.AsObject();

    private static async Task WithRepoAsync(Func<TournamentRepository, Task> body)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_caissadesk_import_{Guid.NewGuid():N}.db");
        try
        {
            await body(new TournamentRepository($"Data Source={dbPath}"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public Task Export_IncludesFormatVersion_AndRoundTrips() => WithRepoAsync(async repo =>
    {
        var t = SampleTournament();
        await repo.SaveAsync(t);

        string json = await repo.ExportJsonAsync(t.Id);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(TournamentRepository.CdeskFormatVersion, root[TournamentRepository.FormatVersionPropertyName]!.GetValue<int>());
        Assert.Equal(TournamentRepository.FormatVersionPropertyName, root.First().Key);

        await repo.DeleteAsync(t.Id);
        var imported = await repo.ImportJsonAsync(json);
        Assert.Equal(t.Id, imported.Id);
        Assert.Equal(3, imported.Players.Count);
        Assert.Equal(2, imported.Rounds[0].Pairings.Count);
    });

    [Fact]
    public Task Import_LegacyFileWithoutFormatVersion_Works() => WithRepoAsync(async repo =>
    {
        string legacyJson = JsonSerializer.Serialize(SampleTournament(), new JsonSerializerOptions { WriteIndented = true });
        Assert.DoesNotContain("formatVersion", legacyJson);

        var imported = await repo.ImportJsonAsync(legacyJson);
        Assert.Equal("Validation Open", imported.Name);
        Assert.NotNull(await repo.GetByIdAsync(imported.Id));
    });

    [Fact]
    public Task Import_EmptyId_AssignsNewId() => WithRepoAsync(async repo =>
    {
        var node = ToNode(SampleTournament());
        node["Id"] = "";
        var imported = await repo.ImportJsonAsync(node.ToJsonString());
        Assert.False(string.IsNullOrWhiteSpace(imported.Id));
    });

    [Fact]
    public void Import_NewerFormatVersion_IsRejected()
    {
        var node = ToNode(SampleTournament());
        node["formatVersion"] = TournamentRepository.CdeskFormatVersion + 1;
        var ex = Assert.Throws<FormatException>(() => TournamentRepository.ParseImport(node.ToJsonString()));
        Assert.Contains("newer version", ex.Message);
    }

    [Theory]
    [InlineData("\"one\"")]
    [InlineData("0")]
    [InlineData("null")]
    public void Import_InvalidFormatVersion_IsRejected(string versionJson)
    {
        var node = ToNode(SampleTournament());
        node["formatVersion"] = JsonNode.Parse(versionJson);
        Assert.Throws<FormatException>(() => TournamentRepository.ParseImport(node.ToJsonString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"Players\": \"oops\"}")]
    public void Import_MalformedContent_ThrowsFormatException(string content)
    {
        Assert.Throws<FormatException>(() => TournamentRepository.ParseImport(content));
    }

    public static IEnumerable<object[]> InvalidMutations()
    {
        yield return [ "null players", (Action<JsonObject>)(n => n["Players"] = null), "player list" ];
        yield return [ "null rounds", (Action<JsonObject>)(n => n["Rounds"] = null), "round list" ];
        yield return [ "blank name", (Action<JsonObject>)(n => n["Name"] = " "), "no name" ];
        yield return [ "null player entry", (Action<JsonObject>)(n => n["Players"]!.AsArray().Add(null)), "Player entry #4" ];
        yield return [ "empty player id", (Action<JsonObject>)(n => n["Players"]![0]!["Id"] = ""), "has no ID" ];
        yield return [ "duplicate player id", (Action<JsonObject>)(n => n["Players"]![1]!["Id"] = n["Players"]![0]!["Id"]!.GetValue<string>()), "Duplicate player ID" ];
        yield return [ "unknown white", (Action<JsonObject>)(n => n["Rounds"]![0]!["Pairings"]![0]!["WhitePlayerId"] = "ghost"), "unknown white player" ];
        yield return [ "unknown black", (Action<JsonObject>)(n => n["Rounds"]![0]!["Pairings"]![0]!["BlackPlayerId"] = "ghost"), "unknown black player" ];
        yield return [ "pairing without players", (Action<JsonObject>)(n =>
            {
                var p = n["Rounds"]![0]!["Pairings"]![1]!;
                p["WhitePlayerId"] = null;
                p["BlackPlayerId"] = null;
            }), "no players" ];
        yield return [ "null pairings", (Action<JsonObject>)(n => n["Rounds"]![0]!["Pairings"] = null), "no pairing list" ];
        yield return [ "zero round number", (Action<JsonObject>)(n => n["Rounds"]![0]!["RoundNumber"] = 0), "must be positive" ];
        yield return [ "negative round number", (Action<JsonObject>)(n => n["Rounds"]![0]!["RoundNumber"] = -2), "must be positive" ];
        yield return [ "duplicate round", (Action<JsonObject>)(n => n["Rounds"]!.AsArray().Add(n["Rounds"]![0]!.DeepClone())), "more than once" ];
        yield return [ "negative total rounds", (Action<JsonObject>)(n => n["TotalRounds"] = -1), "total round count" ];
        yield return [ "absurd total rounds", (Action<JsonObject>)(n => n["TotalRounds"] = 100000), "total round count" ];
        yield return [ "too many pairings", (Action<JsonObject>)(n =>
            {
                var pairings = n["Rounds"]![0]!["Pairings"]!.AsArray();
                var template = pairings[0]!;
                pairings.Add(template.DeepClone());
                pairings.Add(template.DeepClone());
            }), "more pairings" ];
    }

    [Theory]
    [MemberData(nameof(InvalidMutations))]
    public Task Import_InvalidStructure_ThrowsFormatException_AndSavesNothing(string caseName, Action<JsonObject> mutate, string expectedMessagePart)
        => WithRepoAsync(async repo =>
        {
            var node = ToNode(SampleTournament());
            node["formatVersion"] = 1;
            mutate(node);

            var ex = await Assert.ThrowsAsync<FormatException>(() => repo.ImportJsonAsync(node.ToJsonString()));
            Assert.True(ex.Message.Contains(expectedMessagePart, StringComparison.OrdinalIgnoreCase),
                $"[{caseName}] unexpected message: {ex.Message}");
            Assert.Empty(await repo.GetAllSummariesAsync());
        });

    [Fact]
    public void Import_TooManyPlayers_IsRejected()
    {
        var t = new Tournament
        {
            Name = "Huge",
            Players = Enumerable.Range(1, TournamentRepository.MaxImportPlayers + 1)
                .Select(i => new Player { StartRank = i, FullName = $"P{i}" }).ToList()
        };
        var ex = Assert.Throws<FormatException>(() => TournamentRepository.ValidateImportedTournament(t));
        Assert.Contains("too many players", ex.Message);
    }
}
