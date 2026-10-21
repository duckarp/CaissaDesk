using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;

namespace CaissaDesk.Tests;

public class TrfHandlerTests
{
    [Fact]
    public void GenerateTrf_ProducesValidFideTags_AndPlayerLines()
    {
        var tournament = new Tournament
        {
            Name = "Open Bratislava 2026",
            Venue = "Dom Sportu",
            Federation = "SVK",
            ChiefArbiter = "IA Tomas K.",
            TimeControlDetails = "90m + 30s",
            System = TournamentSystem.SwissDutch,
            StartDate = new DateTime(2026, 6, 15),
            EndDate = new DateTime(2026, 6, 21),
            Players =
            [
                new Player { StartRank = 1, FullName = "Carlsen, Magnus", Title = "GM", Federation = "NOR", FideRating = 2832, FideId = "1503014", BirthYear = 1990 },
                new Player { StartRank = 2, FullName = "Caruana, Fabiano", Title = "GM", Federation = "USA", FideRating = 2795, FideId = "2020009", BirthYear = 1992 }
            ],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing
                        {
                            RoundNumber = 1,
                            BoardNumber = 1,
                            WhitePlayerId = null, // assigned below
                            BlackPlayerId = null,
                            Result = MatchResult.Draw
                        }
                    ]
                }
            ]
        };

        var p1 = tournament.Players[0];
        var p2 = tournament.Players[1];
        tournament.Rounds[0].Pairings[0].WhitePlayerId = p1.Id;
        tournament.Rounds[0].Pairings[0].BlackPlayerId = p2.Id;

        string trf = TrfHandler.GenerateTrf(tournament);

        // Check Header tags
        Assert.Contains("012 Open Bratislava 2026", trf);
        Assert.Contains("022 Dom Sportu", trf);
        Assert.Contains("032 SVK", trf);
        Assert.Contains("042 2026/06/15", trf);
        Assert.Contains("052 2026/06/21", trf);
        Assert.Contains("062 2", trf);
        Assert.Contains("092 Individual Swiss", trf);
        Assert.Contains("102 IA Tomas K.", trf);
        Assert.Contains("122 90m + 30s", trf);

        // Check 001 player lines
        Assert.Contains("001    1 mGM  Carlsen, Magnus                   2832 NOR     1503014 1990/00/00  0.5", trf);
        Assert.Contains("001    2 mGM  Caruana, Fabiano                  2795 USA     2020009 1992/00/00  0.5", trf);

        // Check round result format in columns
        Assert.Contains("2 w =", trf); // Carlsen played 2 with white, draw
        Assert.Contains("1 b =", trf); // Caruana played 1 with black, draw
    }

    [Fact]
    public void GenerateTrf_WithByes_FormatsByeResultCharactersCorrectly()
    {
        var tournament = new Tournament
        {
            Name = "Bye Test Open",
            System = TournamentSystem.SwissDutch,
            TotalRounds = 3,
            Players =
            [
                new Player { StartRank = 1, FullName = "Player One", FideRating = 2400 },
                new Player { StartRank = 2, FullName = "Player Two", FideRating = 2300 },
                new Player { StartRank = 3, FullName = "Player Three", FideRating = 2200 }
            ],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin },
                        new Pairing { BoardNumber = 2, WhitePlayerId = "p3", BlackPlayerId = null, Result = MatchResult.ByeFull }
                    ]
                },
                new Round
                {
                    RoundNumber = 2,
                    Pairings =
                    [
                        new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p3", Result = MatchResult.Draw },
                        new Pairing { BoardNumber = 2, WhitePlayerId = "p2", BlackPlayerId = null, Result = MatchResult.ByeHalf }
                    ]
                }
            ]
        };

        tournament.Players[0].Id = "p1";
        tournament.Players[1].Id = "p2";
        tournament.Players[2].Id = "p3";

        string trf = TrfHandler.GenerateTrf(tournament);

        // TRF-16: a pairing-allocated bye is 'U' with opponent "0000" (bbpPairings rejects "   0 - 1")
        Assert.Contains("0000 - U", trf);
        Assert.DoesNotContain("   0 - 1", trf);

        // Player 2 got ½-point bye in round 2 -> "0000 - H"
        Assert.Contains("0000 - H", trf);
    }

    private static Tournament ThreeRoundTournament()
    {
        var t = new Tournament
        {
            Name = "Column Test",
            System = TournamentSystem.SwissDutch,
            TotalRounds = 5,
            Players =
            [
                new Player { Id = "p1", StartRank = 1, FullName = "One" },
                new Player { Id = "p2", StartRank = 2, FullName = "Two" },
                new Player { Id = "p3", StartRank = 3, FullName = "Three" },
                new Player { Id = "p4", StartRank = 4, FullName = "Four" }
            ]
        };
        return t;
    }

    private static string PlayerLine(string trf, int startRank) =>
        trf.Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.StartsWith("001 " + startRank.ToString().PadLeft(4)));

    /// <summary>Round r occupies columns 92+10(r-1) .. 99+10(r-1) (1-based) of a TRF-16 001 line.</summary>
    private static string RoundColumn(string line, int round)
    {
        int start = 91 + 10 * (round - 1);
        return line.Length >= start + 8 ? line.Substring(start, 8) : line.Length > start ? line[start..] : "";
    }

    [Fact]
    public void GenerateTrf_RequestedFullBye_IsWrittenAsF_AndPairingByeAsU()
    {
        var t = ThreeRoundTournament();
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.Draw },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p3", Result = MatchResult.ByeFull },
                new Pairing { BoardNumber = 3, WhitePlayerId = "p4", Result = MatchResult.ByeFullRequested }
            ]
        });

        string trf = TrfHandler.GenerateTrf(t);

        Assert.Equal("0000 - U", RoundColumn(PlayerLine(trf, 3), 1));
        Assert.Equal("0000 - F", RoundColumn(PlayerLine(trf, 4), 1));
        Assert.Contains(" 1.0", PlayerLine(trf, 4)); // requested full bye still scores a point
    }

    [Fact]
    public void GenerateTrf_SkippedRound_KeepsLaterResultsInTheirOwnColumn()
    {
        var t = ThreeRoundTournament();
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p3", BlackPlayerId = "p4", Result = MatchResult.Draw }
            ]
        });
        // Round 2: p4 is not on any board (absent)
        t.Rounds.Add(new Round
        {
            RoundNumber = 2,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p2", BlackPlayerId = "p1", Result = MatchResult.Draw },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p3", Result = MatchResult.ByeFull }
            ]
        });
        t.Rounds.Add(new Round
        {
            RoundNumber = 3,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p4", BlackPlayerId = "p1", Result = MatchResult.BlackWin },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p2", BlackPlayerId = "p3", Result = MatchResult.WhiteForfeit }
            ]
        });

        string trf = TrfHandler.GenerateTrf(t);
        var p4 = PlayerLine(trf, 4);

        Assert.Equal("   3 b =", RoundColumn(p4, 1));
        Assert.Equal("0000 - Z", RoundColumn(p4, 2)); // absence keeps its column
        Assert.Equal("   1 w 0", RoundColumn(p4, 3));

        var p3 = PlayerLine(trf, 3);
        Assert.Equal("   2 b -", RoundColumn(p3, 3)); // forfeit loss
        Assert.Equal("   3 w +", RoundColumn(PlayerLine(trf, 2), 3));
    }

    [Fact]
    public void GenerateTrf_ForPairingRound_MarksWithdrawnAndRequestedByePlayers()
    {
        var t = ThreeRoundTournament();
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p3", Result = MatchResult.WhiteWin },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p4", BlackPlayerId = "p2", Result = MatchResult.Draw }
            ]
        });
        t.Players[1].ByeRequests[2] = ByeType.HalfPoint;  // p2 requests ½ bye in round 2
        t.Players[2].Status = PlayerStatus.Withdrawn;      // p3 withdrew
        t.Players[3].ByeRequests[2] = ByeType.ZeroPoint;  // p4 requests 0 bye

        string trf = TrfHandler.GenerateTrf(t, pairingRound: 2);

        Assert.Equal("0000 - H", RoundColumn(PlayerLine(trf, 2), 2));
        Assert.Equal("0000 - Z", RoundColumn(PlayerLine(trf, 3), 2));
        Assert.Equal("0000 - Z", RoundColumn(PlayerLine(trf, 4), 2));
        Assert.Equal("", RoundColumn(PlayerLine(trf, 1), 2).Trim()); // p1 is to be paired
    }

    [Fact]
    public void GenerateTrf_ForPairingRound_IgnoresAlreadyCreatedPairingsOfThatRound()
    {
        var t = ThreeRoundTournament();
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings = [new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin }]
        });

        string trf = TrfHandler.GenerateTrf(t, pairingRound: 1);

        Assert.Equal("", RoundColumn(PlayerLine(trf, 1), 1).Trim());
    }

    [Fact]
    public void GenerateTrf_WithFoaTitles_FormatsProperlyInTrf()
    {
        var tournament = new Tournament
        {
            Name = "Arena Grand Prix",
            System = TournamentSystem.SwissDutch,
            TotalRounds = 1,
            Players =
            [
                new Player { StartRank = 1, FullName = "Arena Hero", Title = "AGM", Federation = "SVK", FideRating = 1950, FideId = "1490100", BirthYear = 1995, Gender = "M" },
                new Player { StartRank = 2, FullName = "Arena Master", Title = "AIM", Federation = "CZE", FideRating = 1800, FideId = "3090200", BirthYear = 1998, Gender = "F" }
            ],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin }
                    ]
                }
            ]
        };

        tournament.Players[0].Id = "p1";
        tournament.Players[1].Id = "p2";

        string trf = TrfHandler.GenerateTrf(tournament);

        // Player 1 line: Gender 'm', Title 'AGM'
        Assert.Contains("001    1 mAGM Arena Hero                        1950 SVK     1490100 1995/00/00  1.0", trf);
        // Player 2 line: Gender 'w', Title 'AIM'
        Assert.Contains("001    2 wAIM Arena Master                      1800 CZE     3090200 1998/00/00  0.0", trf);
    }
}
