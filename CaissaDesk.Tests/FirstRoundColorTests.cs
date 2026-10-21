using System.Text.Json;
using Caissa.Shared.Models;
using CaissaDesk.Core.PairingEngine;

namespace CaissaDesk.Tests;

public class FirstRoundColorTests
{
    private static Tournament Field(int n, FirstRoundColor color) => new()
    {
        System = TournamentSystem.SwissDutch,
        TotalRounds = 7,
        TopSeedFirstRoundColor = color,
        Players = Enumerable.Range(1, n)
            .Select(r => new Player { Id = $"p{r}", StartRank = r, FullName = $"Player {r}", FideRating = 2500 - 10 * r })
            .ToList()
    };

    private static List<string> TrfLines(Tournament t) =>
        TrfHandler.GenerateTrf(t).Split('\n').Select(l => l.TrimEnd('\r')).ToList();

    [Fact]
    public void NewTournament_DefaultsToWhite()
    {
        Assert.Equal(FirstRoundColor.White, new Tournament().TopSeedFirstRoundColor);
    }

    [Fact]
    public void LegacyJson_WithoutProperty_DeserializesToWhite()
    {
        var legacy = new Tournament { Name = "Legacy" };
        var node = JsonSerializer.SerializeToNode(legacy)!.AsObject();
        Assert.True(node.Remove(nameof(Tournament.TopSeedFirstRoundColor)));

        var restored = JsonSerializer.Deserialize<Tournament>(node.ToJsonString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(restored);
        Assert.Equal("Legacy", restored.Name);
        Assert.Equal(FirstRoundColor.White, restored.TopSeedFirstRoundColor);
    }

    [Fact]
    public void Json_RoundTripsBlack()
    {
        var json = JsonSerializer.Serialize(new Tournament { TopSeedFirstRoundColor = FirstRoundColor.Black });
        Assert.Equal(FirstRoundColor.Black, JsonSerializer.Deserialize<Tournament>(json)!.TopSeedFirstRoundColor);
    }

    [Theory]
    [InlineData(FirstRoundColor.White, "XXC white1")]
    [InlineData(FirstRoundColor.Black, "XXC black1")]
    public void Trf_WritesInitialColour(FirstRoundColor color, string expected)
    {
        var lines = TrfLines(Field(4, color));
        var xxc = Assert.Single(lines, l => l.StartsWith("XXC", StringComparison.Ordinal));
        Assert.Equal(expected, xxc);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void Native_Round1_TopSeedGetsConfiguredColour_AndAlternates(int n)
    {
        foreach (var color in new[] { FirstRoundColor.White, FirstRoundColor.Black })
        {
            var t = Field(n, color);
            var result = new NativeDutchPairingEngine().GenerateRoundPairings(t, 1);
            Assert.True(result.Success, result.ErrorMessage);

            var games = result.Pairings.Where(p => p.BlackPlayerId != null).OrderBy(p => p.BoardNumber).ToList();
            Assert.Equal(n / 2, games.Count);
            for (int i = 0; i < games.Count; i++)
            {
                string topSeed = $"p{i + 1}";
                bool topSeedWhite = (i % 2 == 0) == (color == FirstRoundColor.White);
                Assert.Equal(topSeed, topSeedWhite ? games[i].WhitePlayerId : games[i].BlackPlayerId);
            }
        }
    }

    [Theory]
    [InlineData(6, FirstRoundColor.White)]
    [InlineData(6, FirstRoundColor.Black)]
    [InlineData(9, FirstRoundColor.Black)]
    public void Bbp_Round1_HonoursInitialColour_AndMatchesNative(int n, FirstRoundColor color)
    {
        var bbp = new BbpPairingEngine();
        if (!bbp.IsAvailable) return; // binary not installed in this environment

        var t = Field(n, color);
        var expected = bbp.GenerateRoundPairings(t, 1);
        Assert.True(expected.Success, expected.ErrorMessage);

        var board1 = expected.Pairings.Single(p => p.WhitePlayerId == "p1" || p.BlackPlayerId == "p1");
        Assert.Equal("p1", color == FirstRoundColor.White ? board1.WhitePlayerId : board1.BlackPlayerId);

        var actual = new NativeDutchPairingEngine().GenerateRoundPairings(Field(n, color), 1);
        Assert.True(actual.Success, actual.ErrorMessage);
        static string Key(Pairing p) => $"{p.WhitePlayerId}-{p.BlackPlayerId ?? "bye"}";
        Assert.Equal(
            expected.Pairings.Select(Key).OrderBy(k => k),
            actual.Pairings.Select(Key).OrderBy(k => k));
    }
}
