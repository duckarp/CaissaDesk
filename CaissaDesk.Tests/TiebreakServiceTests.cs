using Caissa.Shared.Tiebreak;
using Caissa.Shared.Models;

namespace CaissaDesk.Tests;

public class TiebreakServiceTests
{
    [Fact]
    public void DirectEncounter_ResolvesTie_WhenOnePlayerWonHeadToHead()
    {
        var service = new TiebreakService();

        var pA = new Player { Id = "A", StartRank = 2, FullName = "Player A" };
        var pB = new Player { Id = "B", StartRank = 1, FullName = "Player B" };
        var pC = new Player { Id = "C", StartRank = 3, FullName = "Player C" };

        var tournament = new Tournament
        {
            Players = [pA, pB, pC],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        // A beats B
                        new Pairing { WhitePlayerId = "A", BlackPlayerId = "B", Result = MatchResult.WhiteWin },
                        // C loses to A
                        new Pairing { WhitePlayerId = "C", BlackPlayerId = "A", Result = MatchResult.BlackWin }
                    ]
                },
                new Round
                {
                    RoundNumber = 2,
                    Pairings =
                    [
                        // B beats C twice (to get 2 points, tying with A who also has 2 points)
                        new Pairing { WhitePlayerId = "B", BlackPlayerId = "C", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "C", BlackPlayerId = "B", Result = MatchResult.BlackWin }
                    ]
                }
            ],
            TiebreakOrder = [TiebreakType.DirectEncounter]
        };

        // A has 2 points (beat B, beat C)
        // B has 2 points (lost to A, beat C x2)
        // C has 0 points
        // Tie between A and B: A beat B head-to-head!
        var standings = service.CalculateStandings(tournament, [TiebreakType.DirectEncounter]);

        Assert.Equal("A", standings[0].PlayerId);
        Assert.Equal(1, standings[0].Rank);
        Assert.Equal("B", standings[1].PlayerId);
        Assert.Equal(2, standings[1].Rank);
        Assert.Equal("C", standings[2].PlayerId);
        Assert.Equal(3, standings[2].Rank);

        // A has 1.0 point against tied player B; B has 0.0 points against tied player A
        Assert.Equal(1.0, standings[0].TiebreakValues[TiebreakType.DirectEncounter]);
        Assert.Equal(0.0, standings[1].TiebreakValues[TiebreakType.DirectEncounter]);
    }

    [Fact]
    public void Buchholz_And_BuchholzCut1_CalculatedAccurately()
    {
        var service = new TiebreakService();

        var pMain = new Player { Id = "M", StartRank = 1, FullName = "Main" };
        var op1 = new Player { Id = "O1", StartRank = 2, FullName = "Op1" }; // will have 4 pts
        var op2 = new Player { Id = "O2", StartRank = 3, FullName = "Op2" }; // will have 3 pts
        var op3 = new Player { Id = "O3", StartRank = 4, FullName = "Op3" }; // will have 2 pts
        var op4 = new Player { Id = "O4", StartRank = 5, FullName = "Op4" }; // will have 1 pt (lowest)

        var tournament = new Tournament
        {
            Players = [pMain, op1, op2, op3, op4],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing { WhitePlayerId = "M", BlackPlayerId = "O1", Result = MatchResult.Draw },
                        new Pairing { WhitePlayerId = "M", BlackPlayerId = "O2", Result = MatchResult.Draw },
                        new Pairing { WhitePlayerId = "M", BlackPlayerId = "O3", Result = MatchResult.Draw },
                        new Pairing { WhitePlayerId = "M", BlackPlayerId = "O4", Result = MatchResult.Draw },
                        // Assign points to opponents:
                        // O1 gets 3.5 more points = 4.0
                        new Pairing { WhitePlayerId = "O1", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O1", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O1", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O1", BlackPlayerId = null, Result = MatchResult.ByeHalf },
                        // O2 gets 2.5 more points = 3.0
                        new Pairing { WhitePlayerId = "O2", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O2", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O2", BlackPlayerId = null, Result = MatchResult.ByeHalf },
                        // O3 gets 1.5 more points = 2.0
                        new Pairing { WhitePlayerId = "O3", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O3", BlackPlayerId = null, Result = MatchResult.ByeHalf },
                        // O4 gets 0.5 more points = 1.0
                        new Pairing { WhitePlayerId = "O4", BlackPlayerId = null, Result = MatchResult.ByeHalf },
                    ]
                }
            ]
        };

        var standings = service.CalculateStandings(tournament);
        var mainStanding = standings.First(s => s.PlayerId == "M");

        // Opponents scores: 4.0, 3.0, 2.0, 1.0
        // Full Buchholz: 4 + 3 + 2 + 1 = 10.0
        Assert.Equal(10.0, mainStanding.TiebreakValues[TiebreakType.Buchholz]);

        // Buchholz Cut 1: excludes lowest (1.0) -> 4 + 3 + 2 = 9.0
        Assert.Equal(9.0, mainStanding.TiebreakValues[TiebreakType.BuchholzCut1]);

        // Buchholz Cut 2: excludes 2 lowest (1.0, 2.0) -> 4 + 3 = 7.0
        Assert.Equal(7.0, mainStanding.TiebreakValues[TiebreakType.BuchholzCut2]);

        // Median Buchholz: excludes lowest (1.0) and highest (4.0) -> 3 + 2 = 5.0
        Assert.Equal(5.0, mainStanding.TiebreakValues[TiebreakType.MedianBuchholz]);
    }

    [Fact]
    public void SonnebornBerger_CalculatesSumOfDefeatedScoresPlusHalfDrawnScores()
    {
        var service = new TiebreakService();

        var p = new Player { Id = "P", StartRank = 1, FullName = "Hero" };
        var beaten = new Player { Id = "B", StartRank = 2, FullName = "Beaten" }; // has 3 pts
        var drawn = new Player { Id = "D", StartRank = 3, FullName = "Drawn" };   // has 2 pts
        var lost = new Player { Id = "L", StartRank = 4, FullName = "Lost" };     // has 4 pts

        var tournament = new Tournament
        {
            Players = [p, beaten, drawn, lost],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "B", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "D", Result = MatchResult.Draw },
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "L", Result = MatchResult.BlackWin },

                        // Give B 3 points
                        new Pairing { WhitePlayerId = "B", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "B", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "B", BlackPlayerId = null, Result = MatchResult.ByeFull },

                        // Give D 1.5 more points = 2.0 total (0.5 from draw with P + 1.5)
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = null, Result = MatchResult.ByeHalf },

                        // Give L 3 more points = 4.0 total (1.0 from win vs P + 3.0)
                        new Pairing { WhitePlayerId = "L", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "L", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "L", BlackPlayerId = null, Result = MatchResult.ByeFull },
                    ]
                }
            ]
        };

        var standings = service.CalculateStandings(tournament);
        var hero = standings.First(s => s.PlayerId == "P");

        // Beaten (B): 3.0 pts * 1.0 = 3.0
        // Drawn (D): 2.0 pts * 0.5 = 1.0
        // Lost (L): 4.0 pts * 0.0 = 0.0
        // Expected SB: 3.0 + 1.0 = 4.0
        Assert.Equal(4.0, hero.TiebreakValues[TiebreakType.SonnebornBerger]);
    }

    [Fact]
    public void MostWins_RanksPlayerWithMoreDecisiveWinsHigher()
    {
        var service = new TiebreakService();

        var pWins = new Player { Id = "W", StartRank = 2, FullName = "3 Wins 1 Loss" };
        var pDraws = new Player { Id = "D", StartRank = 1, FullName = "2 Wins 2 Draws" };
        var dummy = new Player { Id = "X", StartRank = 3, FullName = "Dummy" };

        var tournament = new Tournament
        {
            Players = [pWins, pDraws, dummy],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        // pWins: 3 wins, 1 loss = 3.0 pts
                        new Pairing { WhitePlayerId = "W", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "W", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "W", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "W", BlackPlayerId = "X", Result = MatchResult.BlackWin },

                        // pDraws: 2 wins, 2 draws = 3.0 pts
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = "X", Result = MatchResult.Draw },
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = "X", Result = MatchResult.Draw }
                    ]
                }
            ]
        };

        var standings = service.CalculateStandings(tournament, [TiebreakType.MostWins]);

        Assert.Equal("W", standings[0].PlayerId);
        Assert.Equal(3, standings[0].Wins);
        Assert.Equal("D", standings[1].PlayerId);
        Assert.Equal(2, standings[1].Wins);
    }

    [Fact]
    public void Koya_CalculatesPointsAgainstOpponentsWithAtLeast50PercentScore()
    {
        var service = new TiebreakService();

        var pHero = new Player { Id = "P", StartRank = 1, FullName = "Hero" };
        var opTop = new Player { Id = "O1", StartRank = 2, FullName = "Top (3.0 pts)" };
        var opMid = new Player { Id = "O2", StartRank = 3, FullName = "Mid (2.0 pts)" };
        var opLow = new Player { Id = "O3", StartRank = 4, FullName = "Low (1.0 pt)" };

        var tournament = new Tournament
        {
            TotalRounds = 4, // 50% threshold is 2.0 pts
            Players = [pHero, opTop, opMid, opLow],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        // Hero beats opTop (1.0 pt)
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "O1", Result = MatchResult.WhiteWin },
                        // Hero draws opMid (0.5 pt)
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "O2", Result = MatchResult.Draw },
                        // Hero beats opLow (1.0 pt)
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "O3", Result = MatchResult.WhiteWin },

                        // opTop gets 3.0 total points (lost to P, + 3 bye wins)
                        new Pairing { WhitePlayerId = "O1", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O1", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O1", BlackPlayerId = null, Result = MatchResult.ByeFull },

                        // opMid gets 2.0 total points (0.5 vs P, + 1.5 bye)
                        new Pairing { WhitePlayerId = "O2", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "O2", BlackPlayerId = null, Result = MatchResult.ByeHalf },

                        // opLow gets 1.0 total point (lost to P, + 1.0 bye)
                        new Pairing { WhitePlayerId = "O3", BlackPlayerId = null, Result = MatchResult.ByeFull }
                    ]
                }
            ]
        };

        var standings = service.CalculateStandings(tournament, [TiebreakType.Koya]);
        var hero = standings.First(s => s.PlayerId == "P");

        // Opponents >= 2.0 pts: opTop (3.0 pts) and opMid (2.0 pts)
        // Opponent < 2.0 pts: opLow (1.0 pt) -> excluded from Koya!
        // Hero's score against opTop (1.0) + opMid (0.5) = 1.5
        Assert.Equal(1.5, hero.TiebreakValues[TiebreakType.Koya]);
    }

    [Fact]
    public void ExtendedKoya_BreaksTieBySteppingDownThreshold()
    {
        var service = new TiebreakService();

        // 4 total rounds => 50% threshold is 2.0
        var p1 = new Player { Id = "P1", StartRank = 2, FullName = "Player 1" };
        var p2 = new Player { Id = "P2", StartRank = 1, FullName = "Player 2" };

        var opTop = new Player { Id = "Top", StartRank = 3, FullName = "Top (3.0 pts)" };
        var opMid = new Player { Id = "Mid", StartRank = 4, FullName = "Mid (1.5 pts)" }; // 50% - 0.5 step

        var tournament = new Tournament
        {
            TotalRounds = 4,
            Players = [p1, p2, opTop, opMid],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        // Both P1 and P2 draw Top (0.5 pt each)
                        new Pairing { WhitePlayerId = "P1", BlackPlayerId = "Top", Result = MatchResult.Draw },
                        new Pairing { WhitePlayerId = "P2", BlackPlayerId = "Top", Result = MatchResult.Draw },

                        // P1 beats Mid (1.0 pt)
                        new Pairing { WhitePlayerId = "P1", BlackPlayerId = "Mid", Result = MatchResult.WhiteWin },
                        // P2 draws Mid (0.5 pt)
                        new Pairing { WhitePlayerId = "P2", BlackPlayerId = "Mid", Result = MatchResult.Draw },

                        // Give P2 an extra 0.5 bye so P1 and P2 have identical total points (1.5 pts)
                        new Pairing { WhitePlayerId = "P2", BlackPlayerId = null, Result = MatchResult.ByeHalf },

                        // Top has 3.0 pts (0.5 + 0.5 vs P1/P2 + 2.0 bye)
                        new Pairing { WhitePlayerId = "Top", BlackPlayerId = null, Result = MatchResult.ByeFull },
                        new Pairing { WhitePlayerId = "Top", BlackPlayerId = null, Result = MatchResult.ByeFull },

                        // Mid has 1.5 pts (0.0 + 0.5 vs P1/P2 + 1.0 bye)
                        new Pairing { WhitePlayerId = "Mid", BlackPlayerId = null, Result = MatchResult.ByeFull }
                    ]
                }
            ]
        };

        // At 50% threshold (2.0): both have 0.5 against Top. Tied on standard Koya!
        // At 50% - 0.5 step (1.5): P1 has 0.5 (Top) + 1.0 (Mid) = 1.5 pts. P2 has 0.5 (Top) + 0.5 (Mid) = 1.0 pt.
        // Extended Koya must place P1 above P2!
        var standings = service.CalculateStandings(tournament, [TiebreakType.ExtendedKoya]);

        Assert.Equal("Top", standings[0].PlayerId);
        Assert.Equal("P1", standings[1].PlayerId);
        Assert.Equal("P2", standings[2].PlayerId);
    }

    [Fact]
    public void Aroc_And_Aroc1_CalculatedAccurately()
    {
        var service = new TiebreakService();

        var p = new Player { Id = "P", StartRank = 1, FullName = "Hero" };
        var op1 = new Player { Id = "O1", StartRank = 2, FideRating = 2000 };
        var op2 = new Player { Id = "O2", StartRank = 3, FideRating = 2200 };
        var op3 = new Player { Id = "O3", StartRank = 4, FideRating = 2400 };

        var tournament = new Tournament
        {
            Players = [p, op1, op2, op3],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "O1", Result = MatchResult.Draw },
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "O2", Result = MatchResult.Draw },
                        new Pairing { WhitePlayerId = "P", BlackPlayerId = "O3", Result = MatchResult.Draw }
                    ]
                }
            ]
        };

        var standings = service.CalculateStandings(tournament, [TiebreakType.Aroc, TiebreakType.Aroc1]);
        var hero = standings.First(s => s.PlayerId == "P");

        // AROC uncut: (2000 + 2200 + 2400) / 3 = 2200
        Assert.Equal(2200.0, hero.TiebreakValues[TiebreakType.Aroc]);

        // AROC-1 cut lowest (drops 2000): (2200 + 2400) / 2 = 2300
        Assert.Equal(2300.0, hero.TiebreakValues[TiebreakType.Aroc1]);

        // Verify formatting
        Assert.Equal("2200", hero.FormatTiebreak(TiebreakType.Aroc));
        Assert.Equal("2300", hero.FormatTiebreak(TiebreakType.Aroc1));
    }

    [Fact]
    public void FormatTiebreak_RendersAllCriteriaCorrectly()
    {
        var standing = new PlayerStanding
        {
            TiebreakValues =
            {
                [TiebreakType.DirectEncounter] = 1.0,
                [TiebreakType.Buchholz] = 18.5,
                [TiebreakType.MostWins] = 4.0,
                [TiebreakType.MostBlack] = 3.0,
                [TiebreakType.Aroc] = 2154.2,
                [TiebreakType.Koya] = 2.5
            }
        };

        Assert.Equal("1.0", standing.FormatTiebreak(TiebreakType.DirectEncounter));
        Assert.Equal("18.50", standing.FormatTiebreak(TiebreakType.Buchholz));
        Assert.Equal("4", standing.FormatTiebreak(TiebreakType.MostWins));
        Assert.Equal("3", standing.FormatTiebreak(TiebreakType.MostBlack));
        Assert.Equal("2154", standing.FormatTiebreak(TiebreakType.Aroc));
        Assert.Equal("2.5", standing.FormatTiebreak(TiebreakType.Koya));
    }

    // ---- C.07 (2023) helpers ----

    private static Tournament Swiss(params (string Id, int Rank)[] players) => new()
    {
        System = TournamentSystem.SwissDutch,
        TotalRounds = 5,
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
    public void DirectEncounter_ThreeWayCycle_IsConsistent_AndFallsThroughToNextCriterion()
    {
        var service = new TiebreakService();
        // A, B, C, D all finish on 3 points. Mini-tournament: A beat B, C and D; B beat C, C beat D, D beat B.
        var tournament = new Tournament
        {
            Players =
            [
                new Player { Id = "A", StartRank = 4, FullName = "A" },
                new Player { Id = "B", StartRank = 3, FullName = "B" },
                new Player { Id = "C", StartRank = 2, FullName = "C" },
                new Player { Id = "D", StartRank = 1, FullName = "D" },
                new Player { Id = "X", StartRank = 5, FullName = "X" }
            ],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing { WhitePlayerId = "A", BlackPlayerId = "B", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "A", BlackPlayerId = "C", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "A", BlackPlayerId = "D", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "B", BlackPlayerId = "C", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "C", BlackPlayerId = "D", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = "B", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "B", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "B", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "C", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "C", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = "X", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "D", BlackPlayerId = "X", Result = MatchResult.WhiteWin }
                    ]
                }
            ]
        };

        var standings = service.CalculateStandings(tournament, [TiebreakType.DirectEncounter]);

        // A wins the mini-tournament (3 points). B, C, D are re-evaluated among themselves (6.2): 1 point
        // each, still tied, so the next criterion (here: starting rank) decides: D, C, B.
        Assert.Equal(["A", "D", "C", "B", "X"], standings.Select(s => s.PlayerId).ToArray());
    }

    [Fact]
    public void DirectEncounter_NotAllMet_RanksGuaranteedLeaderFirst()
    {
        var service = new TiebreakService();
        var t = Swiss(("A", 3), ("B", 1), ("C", 2), ("X", 4), ("Y", 5));
        // A, B and C end on 2 points. A beat B and C; B and C never met.
        Round(t, 1, ("A", "B", MatchResult.WhiteWin), ("C", "X", MatchResult.WhiteWin), ("Y", null, MatchResult.ByeZero));
        Round(t, 2, ("C", "A", MatchResult.BlackWin), ("B", "X", MatchResult.WhiteWin), ("Y", null, MatchResult.ByeZero));
        Round(t, 3, ("B", "Y", MatchResult.WhiteWin), ("C", "Y", MatchResult.WhiteWin), ("A", "X", MatchResult.BlackWin));

        var standings = service.CalculateStandings(t, [TiebreakType.DirectEncounter]);

        Assert.Equal("A", standings[0].PlayerId); // 2 DE points; B and C at most 1 even if they had met (6.3)
        Assert.Equal("B", standings[1].PlayerId); // B vs C undecided by DE -> starting rank
        Assert.Equal("C", standings[2].PlayerId);
    }

    [Fact]
    public void DirectEncounter_IgnoresForfeitsInSwiss()
    {
        var service = new TiebreakService();
        var t = Swiss(("A", 2), ("B", 1));
        Round(t, 1, ("A", "B", MatchResult.WhiteForfeit));
        Round(t, 2, ("B", "A", MatchResult.WhiteForfeit));

        var standings = service.CalculateStandings(t, [TiebreakType.DirectEncounter]);

        Assert.Equal("B", standings[0].PlayerId); // no over-the-board encounter: starting rank decides
        Assert.Equal(0.0, Of(standings, "A").TiebreakValues[TiebreakType.DirectEncounter]);
    }

    [Fact]
    public void Buchholz_PairingAllocatedBye_CountsAsDummyOpponentWithOwnScore()
    {
        var service = new TiebreakService();
        var t = Swiss(("P", 1), ("X", 2), ("Y", 3), ("Z", 4));
        Round(t, 1, ("P", null, MatchResult.ByeFull), ("X", "Y", MatchResult.WhiteWin));   // Z absent
        Round(t, 2, ("P", "X", MatchResult.WhiteWin), ("Y", "Z", MatchResult.Draw));
        Round(t, 3, ("Y", "P", MatchResult.Draw), ("X", "Z", MatchResult.WhiteWin));

        var standings = service.CalculateStandings(t);
        var p = Of(standings, "P");

        // P: 1 (PAB) + 1 + ½ = 2.5. X = 2, Y = 1.
        // BH = dummy(2.5) + X(2) + Y(1) = 5.5 (C.07 16.4)
        Assert.Equal(2.5, p.Points);
        Assert.Equal(5.5, p.TiebreakValues[TiebreakType.Buchholz]);
        // SB = 1×2.5 (PAB vs dummy) + 1×2 + ½×1 = 5.0
        Assert.Equal(5.0, p.TiebreakValues[TiebreakType.SonnebornBerger]);
        // WIN counts the PAB (7.1), BPG counts only over-the-board Black games (7.3)
        Assert.Equal(2, p.Wins);
        Assert.Equal(1, p.BlackGames);
    }

    [Fact]
    public void Buchholz_ForfeitWin_UsesDummy_AndForfeitOpponentIsNotInAroOrBlackGames()
    {
        var service = new TiebreakService();
        var t = Swiss(("P", 1), ("F", 2), ("X", 3), ("Y", 4));
        t.Players.Single(p => p.Id == "F").FideRating = 1000;
        t.Players.Single(p => p.Id == "X").FideRating = 2000;
        t.Players.Single(p => p.Id == "Y").FideRating = 2200;

        Round(t, 1, ("F", "P", MatchResult.BlackForfeit), ("X", "Y", MatchResult.Draw));
        Round(t, 2, ("X", "P", MatchResult.Draw), ("Y", "F", MatchResult.WhiteWin));
        Round(t, 3, ("P", "Y", MatchResult.Draw), ("F", "X", MatchResult.BlackWin));

        var standings = service.CalculateStandings(t, [TiebreakType.Buchholz, TiebreakType.Aroc, TiebreakType.MostBlack]);
        var p = Of(standings, "P");

        // P = 1 (forfeit win) + ½ + ½ = 2. X = 2, Y = 2, F = 0.
        // BH = dummy(2) + X(2) + Y(2) = 6, the forfeiting F does not contribute
        Assert.Equal(6.0, p.TiebreakValues[TiebreakType.Buchholz]);
        // ARO over the board only: (2000 + 2200) / 2
        Assert.Equal(2100.0, p.TiebreakValues[TiebreakType.Aroc]);
        // Black over the board: only round 2 (the forfeit as Black does not count)
        Assert.Equal(1.0, p.TiebreakValues[TiebreakType.MostBlack]);
    }

    [Fact]
    public void Buchholz_OpponentsTrailingRequestedByes_CountAsDraws()
    {
        var service = new TiebreakService();
        var t = Swiss(("P", 1), ("Q", 2), ("R", 3), ("S", 4));
        Round(t, 1, ("P", "Q", MatchResult.WhiteWin), ("R", "S", MatchResult.WhiteWin));
        // Q requests a zero-point bye in round 2 and does not play again (withdraws: absent in round 3)
        Round(t, 2, ("P", "R", MatchResult.Draw), ("Q", null, MatchResult.ByeZero), ("S", null, MatchResult.ByeFull));
        Round(t, 3, ("S", "P", MatchResult.Draw), ("R", null, MatchResult.ByeFull));

        var standings = service.CalculateStandings(t);
        var p = Of(standings, "P");

        // Q scored 0, but its two trailing unplayed rounds (16.2.5) count as draws for P's Buchholz: 1.0
        // R = 1 + ½ + 1 = 2.5, S = 0 + 1 + ½ = 1.5  ->  BH(P) = 1 + 2.5 + 1.5 = 5
        Assert.Equal(0.0, Of(standings, "Q").Points);
        Assert.Equal(5.0, p.TiebreakValues[TiebreakType.Buchholz]);
    }

    [Fact]
    public void Buchholz_RequestedByeFollowedByPlay_KeepsAwardedResult()
    {
        var service = new TiebreakService();
        var t = Swiss(("P", 1), ("Q", 2), ("R", 3), ("S", 4));
        Round(t, 1, ("Q", null, MatchResult.ByeHalf), ("P", "R", MatchResult.WhiteWin), ("S", null, MatchResult.ByeFull));
        Round(t, 2, ("P", "Q", MatchResult.WhiteWin), ("R", "S", MatchResult.Draw));

        var standings = service.CalculateStandings(t);

        // Q: ½ (requested bye, later played: 16.2.3, awarded) + 0 = ½; R = ½
        Assert.Equal(1.0, Of(standings, "P").TiebreakValues[TiebreakType.Buchholz]);
    }

    [Fact]
    public void BuchholzCut1_CutsVoluntaryUnplayedRoundFirst()
    {
        var service = new TiebreakService();
        var t = Swiss(("P", 1), ("W", 2), ("X", 3), ("Y", 4), ("Z", 5));
        // P takes a ½-point bye in round 1, then wins two games
        Round(t, 1, ("P", null, MatchResult.ByeHalf), ("W", "X", MatchResult.WhiteWin), ("Y", "Z", MatchResult.WhiteWin));
        Round(t, 2, ("P", "X", MatchResult.WhiteWin), ("W", "Y", MatchResult.WhiteWin), ("Z", null, MatchResult.ByeFull));
        Round(t, 3, ("Z", "P", MatchResult.BlackWin), ("W", null, MatchResult.ByeFull), ("X", "Y", MatchResult.Draw));

        var standings = service.CalculateStandings(t);
        var p = Of(standings, "P");

        // P = 2.5. Contributions: bye -> dummy 2.5 (voluntary), X = ½, Z = 1.
        Assert.Equal(4.0, p.TiebreakValues[TiebreakType.Buchholz]);
        // 16.5: the voluntary unplayed round (2.5) is cut, not the least significant value (½)
        Assert.Equal(1.5, p.TiebreakValues[TiebreakType.BuchholzCut1]);
    }

    [Fact]
    public void Standings_AreDeterministic_RegardlessOfPlayerOrder()
    {
        var service = new TiebreakService();
        var t = Swiss(("A", 1), ("B", 2), ("C", 3), ("D", 4));
        Round(t, 1, ("A", "B", MatchResult.WhiteWin), ("C", "D", MatchResult.WhiteWin));
        Round(t, 2, ("B", "C", MatchResult.WhiteWin), ("D", "A", MatchResult.WhiteWin));
        Round(t, 3, ("C", "A", MatchResult.WhiteWin), ("B", "D", MatchResult.WhiteWin));

        var first = service.CalculateStandings(t).Select(s => s.PlayerId).ToList();
        t.Players.Reverse();
        var second = service.CalculateStandings(t).Select(s => s.PlayerId).ToList();

        Assert.Equal(first, second);
    }
}
