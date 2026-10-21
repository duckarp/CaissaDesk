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

        // Player 3 got 1-point bye in round 1 -> "   0 - 1"
        Assert.Contains("   0 - 1", trf);

        // Player 2 got ½-point bye in round 2 -> "   0 - H"
        Assert.Contains("   0 - H", trf);
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
