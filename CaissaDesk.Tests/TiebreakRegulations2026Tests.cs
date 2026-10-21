using Caissa.Shared.Models;
using Caissa.Shared.Tiebreak;

namespace CaissaDesk.Tests;

/// <summary>
/// FIDE C.07 Play-Off and Tie-Break Regulations, effective 1 March 2026
/// (https://handbook.fide.com/chapter/TieBreakRegulations032026). Expected values are hand-computed.
/// </summary>
public class TiebreakRegulations2026Tests
{
    private static Tournament Event(TournamentSystem system, int totalRounds, params (string Id, int Rank)[] players) => new()
    {
        System = system,
        TotalRounds = totalRounds,
        Players = players.Select(p => new Player { Id = p.Id, StartRank = p.Rank, FullName = p.Id }).ToList()
    };

    private static void Round(Tournament t, int number, params (string? White, string? Black, MatchResult Result)[] games) =>
        t.Rounds.Add(new Round
        {
            RoundNumber = number,
            Pairings = games.Select((g, i) => new Pairing
            {
                RoundNumber = number,
                BoardNumber = i + 1,
                WhitePlayerId = g.White,
                BlackPlayerId = g.Black,
                Result = g.Result
            }).ToList()
        });

    private static PlayerStanding Of(List<PlayerStanding> standings, string id) => standings.Single(s => s.PlayerId == id);

    [Fact]
    public void Art16_4_1_ForfeitLoss_DummyCappedAtScheduledOpponentsAdjustedScore()
    {
        var service = new TiebreakService();
        var t = Event(TournamentSystem.SwissDutch, 3, ("P", 1), ("F", 2), ("X", 3), ("Y", 4), ("Z", 5), ("W", 6));
        // P forfeits round 1 against F (Black wins by forfeit), then wins two games
        Round(t, 1, ("P", "F", MatchResult.BlackForfeit), ("X", "Y", MatchResult.WhiteWin), ("Z", "W", MatchResult.Draw));
        Round(t, 2, ("P", "Y", MatchResult.WhiteWin), ("F", "X", MatchResult.BlackWin), ("Z", "W", MatchResult.BlackWin));
        Round(t, 3, ("P", "Z", MatchResult.WhiteWin), ("F", "W", MatchResult.BlackWin), ("X", "Y", MatchResult.Draw));

        var standings = service.CalculateStandings(t, [TiebreakType.Buchholz]);
        var p = Of(standings, "P");

        // P = 2, F = 1, Y = ½, Z = ½. Forfeit dummy = min(2, F 1) = 1 (2023 text: 2).
        Assert.Equal(2.0, p.Points);
        Assert.Equal(1.0, Of(standings, "F").Points);
        // BH = 1 + ½ + ½ = 2
        Assert.Equal(2.0, p.TiebreakValues[TiebreakType.Buchholz]);
        // 16.5.1: the VUR contribution (1) is cut first, it is not lower than the least significant value (½)
        Assert.Equal(1.0, p.TiebreakValues[TiebreakType.BuchholzCut1]);
        // SB = 0×1 + 1×½ + 1×½ = 1
        Assert.Equal(1.0, p.TiebreakValues[TiebreakType.SonnebornBerger]);
    }

    [Fact]
    public void Art16_4_1_ForfeitWin_DummyCappedAtScheduledOpponentsAdjustedScore()
    {
        var service = new TiebreakService();
        var t = Event(TournamentSystem.SwissDutch, 3, ("P", 1), ("F", 2), ("X", 3), ("Y", 4));
        Round(t, 1, ("F", "P", MatchResult.BlackForfeit), ("X", "Y", MatchResult.Draw));
        Round(t, 2, ("X", "P", MatchResult.Draw), ("Y", "F", MatchResult.WhiteWin));
        Round(t, 3, ("P", "Y", MatchResult.Draw), ("F", "X", MatchResult.BlackWin));

        var standings = service.CalculateStandings(t, [TiebreakType.SonnebornBerger]);
        var p = Of(standings, "P");

        // P = 2, X = 2, Y = 2, F = 0. Dummy for the forfeit win = min(2, 0) = 0.
        // SB = 1×0 + ½×2 + ½×2 = 2 (2023 text: 1×2 + 1 + 1 = 4)
        Assert.Equal(2.0, p.TiebreakValues[TiebreakType.SonnebornBerger]);
        Assert.Equal(4.0, p.TiebreakValues[TiebreakType.Buchholz]);
    }

    [Fact]
    public void Art16_4_2_PairingAllocatedBye_DummyCappedAtDrawTimesRounds()
    {
        var service = new TiebreakService();
        var t = Event(TournamentSystem.SwissDutch, 3, ("P", 1), ("A", 2), ("B", 3), ("C", 4), ("D", 5));
        Round(t, 1, ("P", null, MatchResult.ByeFull), ("A", "B", MatchResult.WhiteWin), ("C", "D", MatchResult.WhiteWin));
        Round(t, 2, ("P", "B", MatchResult.WhiteWin), ("A", "C", MatchResult.WhiteWin), ("D", null, MatchResult.ByeFull));
        Round(t, 3, ("P", "D", MatchResult.WhiteWin), ("B", "C", MatchResult.Draw), ("A", null, MatchResult.ByeFull));

        var standings = service.CalculateStandings(t, [TiebreakType.Buchholz]);
        var p = Of(standings, "P");

        // P = 3, B = ½, D = 1. Cap = ½ × 3 rounds = 1.5, so the PAB dummy scores min(3, 1.5) = 1.5 (2023 text: 3)
        Assert.Equal(3.0, p.Points);
        // BH = 1.5 + ½ + 1 = 3
        Assert.Equal(3.0, p.TiebreakValues[TiebreakType.Buchholz]);
        // SB = 1×1.5 + 1×½ + 1×1 = 3
        Assert.Equal(3.0, p.TiebreakValues[TiebreakType.SonnebornBerger]);
    }

    [Fact]
    public void Art16_4_2_RequestedHalfBye_DummyCappedAtDrawTimesRounds()
    {
        var service = new TiebreakService();
        var t = Event(TournamentSystem.SwissDutch, 2, ("P", 1), ("A", 2), ("B", 3));
        // P takes a ½-point bye in round 1 (16.2.3), then beats A. B gets the PAB in round 2.
        Round(t, 1, ("P", null, MatchResult.ByeHalf), ("A", "B", MatchResult.WhiteWin));
        Round(t, 2, ("P", "A", MatchResult.WhiteWin), ("B", null, MatchResult.ByeFull));

        var standings = service.CalculateStandings(t, [TiebreakType.Buchholz]);
        var p = Of(standings, "P");

        // P = 1.5, A = 1. Cap = ½ × 2 = 1, dummy = min(1.5, 1) = 1. BH = 1 + 1 = 2 (2023 text: 2.5)
        Assert.Equal(2.0, p.TiebreakValues[TiebreakType.Buchholz]);
        // SB = ½×1 + 1×1 = 1.5
        Assert.Equal(1.5, p.TiebreakValues[TiebreakType.SonnebornBerger]);
    }

    [Fact]
    public void Art10_1_Aro_RoundedToNearestWholeNumber_HalfUp()
    {
        var service = new TiebreakService();
        var t = Event(TournamentSystem.SwissDutch, 3, ("P", 1), ("O1", 2), ("O2", 3), ("O3", 4));
        t.Players.Single(x => x.Id == "O1").FideRating = 2000;
        t.Players.Single(x => x.Id == "O2").FideRating = 2001;
        t.Players.Single(x => x.Id == "O3").FideRating = 2003;
        Round(t, 1, ("P", "O1", MatchResult.Draw), ("O2", "O3", MatchResult.Draw));
        Round(t, 2, ("P", "O2", MatchResult.Draw), ("O1", "O3", MatchResult.Draw));

        var standings = service.CalculateStandings(t, [TiebreakType.Aroc]);
        var p = Of(standings, "P");

        // (2000 + 2001) / 2 = 2000.5 -> 2001; ARO-C1 drops 2000 -> 2001
        Assert.Equal(2001.0, p.TiebreakValues[TiebreakType.Aroc]);
        Assert.Equal(2001.0, p.TiebreakValues[TiebreakType.Aroc1]);
        // O3: (2001 + 2000) / 2 = 2000.5 -> 2001
        Assert.Equal(2001.0, Of(standings, "O3").TiebreakValues[TiebreakType.Aroc]);
    }

    [Fact]
    public void Art7_1_And_7_2_Win_CountsUnplayedWins_Won_OnlyOverTheBoard()
    {
        var service = new TiebreakService();
        var t = Event(TournamentSystem.SwissDutch, 3, ("P", 1), ("A", 2), ("B", 3), ("C", 4));
        Round(t, 1, ("P", null, MatchResult.ByeFull), ("A", "B", MatchResult.WhiteWin));          // C absent
        Round(t, 2, ("C", "P", MatchResult.BlackForfeit), ("A", "B", MatchResult.Draw));
        Round(t, 3, ("P", "B", MatchResult.WhiteWin), ("A", "C", MatchResult.WhiteWin));

        var standings = service.CalculateStandings(t, [TiebreakType.MostWins, TiebreakType.GamesWon]);
        var p = Of(standings, "P");

        // WIN (7.1): PAB + forfeit win + game = 3. WON (7.2): only the round 3 game = 1.
        Assert.Equal(3.0, p.TiebreakValues[TiebreakType.MostWins]);
        Assert.Equal(1.0, p.TiebreakValues[TiebreakType.GamesWon]);
        Assert.Equal(1, p.GamesWon);
        Assert.Equal("1", p.FormatTiebreak(TiebreakType.GamesWon));
        // A won two games over the board
        Assert.Equal(2.0, Of(standings, "A").TiebreakValues[TiebreakType.GamesWon]);
    }

    [Fact]
    public void GamesWon_SeparatesPlayersTiedOnWin()
    {
        var service = new TiebreakService();
        // P and Q both 2 points with 2 WIN; P's wins include a PAB, Q won both games over the board.
        var t = Event(TournamentSystem.SwissDutch, 2, ("P", 1), ("Q", 2), ("R", 3), ("S", 4), ("T", 5));
        Round(t, 1, ("P", null, MatchResult.ByeFull), ("Q", "R", MatchResult.WhiteWin), ("S", "T", MatchResult.Draw));
        Round(t, 2, ("P", "S", MatchResult.WhiteWin), ("T", "Q", MatchResult.BlackWin), ("R", null, MatchResult.ByeFull));

        var standings = service.CalculateStandings(t, [TiebreakType.MostWins, TiebreakType.GamesWon]);

        Assert.Equal("Q", standings[0].PlayerId);
        Assert.Equal("P", standings[1].PlayerId);
    }

    [Fact]
    public void Art15_2_RoundRobin_ExtendedKoya_CountsForfeitsLikeKoya()
    {
        var service = new TiebreakService();
        var t = Event(TournamentSystem.RoundRobin, 3, ("A", 2), ("B", 1), ("C", 3), ("D", 4));
        Round(t, 1, ("A", "B", MatchResult.Draw), ("C", "D", MatchResult.WhiteWin));
        Round(t, 2, ("A", "C", MatchResult.WhiteForfeit), ("B", "D", MatchResult.WhiteWin));
        Round(t, 3, ("D", "A", MatchResult.WhiteWin), ("B", "C", MatchResult.BlackWin));

        var standings = service.CalculateStandings(t, [TiebreakType.ExtendedKoya]);

        // A = 1.5, B = 1.5, C = 2, D = 1. Threshold 50% of 3 = 1.5.
        // 15.2: the forfeit A-C is a regular game. A: ½ (vs B) + 1 (vs C) = 1.5; B: ½ (vs A) + 0 (vs C) = ½.
        Assert.Equal(1.5, Of(standings, "A").TiebreakValues[TiebreakType.Koya]);
        Assert.Equal(0.5, Of(standings, "B").TiebreakValues[TiebreakType.Koya]);
        // Extended Koya must agree with Koya at the first threshold, so A ranks above the better-seeded B
        Assert.Equal(["C", "A", "B", "D"], standings.Select(s => s.PlayerId).ToArray());
    }
}
