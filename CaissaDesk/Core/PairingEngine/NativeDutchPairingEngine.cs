using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public class NativeDutchPairingEngine : IPairingEngine
{
    public string Id => "native_dutch";
    public string DisplayName => "Native C# FIDE Dutch Swiss Engine";
    public bool IsAvailable => true;

    private sealed class PlayerHistory
    {
        public Player Player { get; set; } = default!;
        public double Points { get; set; }
        public HashSet<string> OpponentIds { get; set; } = [];
        public List<char> Colors { get; set; } = []; // 'W' or 'B'
        public bool ReceivedBye { get; set; }

        public int ColorBalance => Colors.Count(c => c == 'W') - Colors.Count(c => c == 'B');

        public bool CanHaveWhite
        {
            get
            {
                if (Colors.Count >= 2 && Colors[^1] == 'W' && Colors[^2] == 'W') return false; // Max 2 consecutive same color
                if (ColorBalance >= 2) return false;
                return true;
            }
        }

        public bool CanHaveBlack
        {
            get
            {
                if (Colors.Count >= 2 && Colors[^1] == 'B' && Colors[^2] == 'B') return false; // Max 2 consecutive same color
                if (ColorBalance <= -2) return false;
                return true;
            }
        }

        public char PreferredColor
        {
            get
            {
                if (ColorBalance > 0) return 'B';
                if (ColorBalance < 0) return 'W';
                if (Colors.Count > 0) return Colors[^1] == 'W' ? 'B' : 'W';
                return 'W';
            }
        }
    }

    public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber)
    {
        var activePlayers = tournament.Players
            .Where(p => p.Status == PlayerStatus.Active)
            .OrderBy(p => p.StartRank)
            .ToList();

        if (activePlayers.Count == 0)
        {
            return PairingResult.Fail("No active players available to pair.");
        }

        // Validate that all games in preceding rounds have recorded results before pairing the next round
        if (roundNumber > 1 && tournament.Rounds.Count > 0)
        {
            var priorRounds = tournament.Rounds
                .Where(r => r.RoundNumber < roundNumber)
                .OrderBy(r => r.RoundNumber)
                .ToList();

            foreach (var r in priorRounds)
            {
                int uncompleted = r.Pairings.Count(p => p.Result == MatchResult.NotPlayed);
                if (uncompleted > 0)
                {
                    return PairingResult.Fail($"Cannot pair Round {roundNumber}: Round {r.RoundNumber} still has {uncompleted} game(s) with results pending.");
                }
            }
        }

        // Separate players who requested an advance bye for this specific round
        var requestedByePairings = new List<Pairing>();
        var playersToPair = new List<Player>();

        foreach (var p in activePlayers)
        {
            if (p.ByeRequests.TryGetValue(roundNumber, out var byeType) && byeType != ByeType.None)
            {
                var matchResult = byeType switch
                {
                    ByeType.HalfPoint => MatchResult.ByeHalf,
                    ByeType.ZeroPoint => MatchResult.ByeZero,
                    ByeType.FullPoint => MatchResult.ByeFull,
                    _ => MatchResult.ByeZero
                };

                var remarks = byeType switch
                {
                    ByeType.HalfPoint => "½-Point Bye (Requested)",
                    ByeType.ZeroPoint => "0-Point Bye (Requested)",
                    ByeType.FullPoint => "1-Point Bye (Requested)",
                    _ => "Bye"
                };

                requestedByePairings.Add(new Pairing
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RoundNumber = roundNumber,
                    WhitePlayerId = p.Id,
                    BlackPlayerId = null,
                    Result = matchResult,
                    Remarks = remarks
                });
            }
            else
            {
                playersToPair.Add(p);
            }
        }

        if (playersToPair.Count == 0 && requestedByePairings.Count == 0)
        {
            return PairingResult.Fail("At least 1 active player is required to generate pairings.");
        }

        List<Pairing> pairings;

        if (playersToPair.Count < 2)
        {
            pairings = new List<Pairing>();
            if (playersToPair.Count == 1)
            {
                // Single remaining player gets a full 1-point pairing bye
                pairings.Add(new Pairing
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RoundNumber = roundNumber,
                    BoardNumber = 1,
                    WhitePlayerId = playersToPair[0].Id,
                    BlackPlayerId = null,
                    Result = MatchResult.ByeFull,
                    Remarks = "1-Point Bye (Unpaired)"
                });
            }
        }
        else
        {
            // Pair playersToPair
            if (roundNumber == 1 || tournament.Rounds.Count == 0)
            {
                var r1Result = GenerateRound1(playersToPair);
                if (!r1Result.Success) return r1Result;
                pairings = r1Result.Pairings;
            }
            else
            {
                var histories = BuildHistories(tournament, playersToPair);
                var subResult = GenerateSubsequentRound(histories, roundNumber);
                if (!subResult.Success) return subResult;
                pairings = subResult.Pairings;
            }
        }

        // Renumber boards and append requested byes at the end
        int nextBoardNumber = pairings.Count > 0 ? pairings.Max(p => p.BoardNumber) + 1 : 1;
        foreach (var byePairing in requestedByePairings)
        {
            byePairing.BoardNumber = nextBoardNumber++;
            pairings.Add(byePairing);
        }

        return PairingResult.Ok(pairings);
    }

    private static PairingResult GenerateRound1(List<Player> activePlayers)
    {
        var pairings = new List<Pairing>();
        int boardNumber = 1;

        var players = new List<Player>(activePlayers);
        Pairing? byePairing = null;

        // If odd, lowest-seeded player receives the 1-point bye
        if (players.Count % 2 != 0)
        {
            var byePlayer = players[^1];
            players.RemoveAt(players.Count - 1);

            byePairing = new Pairing
            {
                Id = Guid.NewGuid().ToString("N"),
                RoundNumber = 1,
                WhitePlayerId = byePlayer.Id,
                BlackPlayerId = null,
                Result = MatchResult.ByeFull,
                Remarks = "1-Point Bye"
            };
        }

        int k = players.Count / 2;
        var topHalf = players.Take(k).ToList();
        var bottomHalf = players.Skip(k).ToList();

        for (int i = 0; i < k; i++)
        {
            var pTop = topHalf[i];
            var pBottom = bottomHalf[i];

            // FIDE standard color alternation on consecutive boards:
            // Board 1: pTop is White
            // Board 2: pBottom is White (pTop is Black)
            // Board 3: pTop is White ...
            bool topIsWhite = (i % 2 == 0);

            pairings.Add(new Pairing
            {
                Id = Guid.NewGuid().ToString("N"),
                RoundNumber = 1,
                BoardNumber = boardNumber++,
                WhitePlayerId = topIsWhite ? pTop.Id : pBottom.Id,
                BlackPlayerId = topIsWhite ? pBottom.Id : pTop.Id,
                Result = MatchResult.NotPlayed
            });
        }

        if (byePairing != null)
        {
            byePairing.BoardNumber = boardNumber;
            pairings.Add(byePairing);
        }

        return PairingResult.Ok(pairings);
    }

    private static PairingResult GenerateSubsequentRound(List<PlayerHistory> histories, int roundNumber)
    {
        var pool = new List<PlayerHistory>(histories);
        var pairings = new List<Pairing>();
        int boardNumber = 1;
        Pairing? byePairing = null;

        // 1. If pool has odd number of players, allocate bye to the lowest eligible player
        if (pool.Count % 2 != 0)
        {
            var byeCandidate = pool
                .Where(h => !h.ReceivedBye)
                .OrderByDescending(h => h.Player.StartRank) // lowest seed
                .FirstOrDefault();

            if (byeCandidate == null)
            {
                // Fallback to lowest seed if all received byes
                byeCandidate = pool.OrderByDescending(h => h.Player.StartRank).First();
            }

            pool.Remove(byeCandidate);
            byePairing = new Pairing
            {
                Id = Guid.NewGuid().ToString("N"),
                RoundNumber = roundNumber,
                WhitePlayerId = byeCandidate.Player.Id,
                BlackPlayerId = null,
                Result = MatchResult.ByeFull,
                Remarks = "1-Point Bye"
            };
        }

        // 2. Score bracket matching with backtracking
        var matchedPairs = MatchBrackets(pool);
        if (matchedPairs == null)
        {
            return PairingResult.Fail($"Could not find valid FIDE Swiss pairings for Round {roundNumber} without rematches. Consider arbiter manual adjustment.");
        }

        foreach (var (white, black) in matchedPairs)
        {
            pairings.Add(new Pairing
            {
                Id = Guid.NewGuid().ToString("N"),
                RoundNumber = roundNumber,
                BoardNumber = boardNumber++,
                WhitePlayerId = white.Player.Id,
                BlackPlayerId = black.Player.Id,
                Result = MatchResult.NotPlayed
            });
        }

        if (byePairing != null)
        {
            byePairing.BoardNumber = boardNumber;
            pairings.Add(byePairing);
        }

        return PairingResult.Ok(pairings);
    }

    private static List<(PlayerHistory White, PlayerHistory Black)>? MatchBrackets(List<PlayerHistory> players)
    {
        // Sort players by points descending, then starting rank ascending
        var sorted = players
            .OrderByDescending(p => p.Points)
            .ThenBy(p => p.Player.StartRank)
            .ToList();

        var matches = new List<(PlayerHistory White, PlayerHistory Black)>();
        if (BacktrackPair(sorted, matches))
        {
            return matches;
        }

        return null;
    }

    private static bool BacktrackPair(List<PlayerHistory> remaining, List<(PlayerHistory White, PlayerHistory Black)> matches)
    {
        if (remaining.Count == 0) return true;

        var p1 = remaining[0];

        // Find candidate opponents for p1
        for (int i = 1; i < remaining.Count; i++)
        {
            var p2 = remaining[i];

            // Absolute Criterion 1: No rematches
            if (p1.OpponentIds.Contains(p2.Player.Id)) continue;

            // Check if valid colors can be assigned
            bool canP1White = p1.CanHaveWhite && p2.CanHaveBlack;
            bool canP2White = p2.CanHaveWhite && p1.CanHaveBlack;

            if (!canP1White && !canP2White) continue;

            // Decide color preference
            PlayerHistory white, black;
            if (canP1White && canP2White)
            {
                // Both can have both colors: prioritize player with stronger preference or higher imbalance
                if (p1.PreferredColor == 'W' && p2.PreferredColor == 'B')
                {
                    white = p1; black = p2;
                }
                else if (p1.PreferredColor == 'B' && p2.PreferredColor == 'W')
                {
                    white = p2; black = p1;
                }
                else if (p1.ColorBalance < p2.ColorBalance)
                {
                    white = p1; black = p2;
                }
                else
                {
                    white = p2; black = p1;
                }
            }
            else if (canP1White)
            {
                white = p1; black = p2;
            }
            else
            {
                white = p2; black = p1;
            }

            // Tentatively match
            matches.Add((white, black));
            var nextRemaining = new List<PlayerHistory>(remaining);
            nextRemaining.RemoveAt(i);
            nextRemaining.RemoveAt(0);

            if (BacktrackPair(nextRemaining, matches))
            {
                return true;
            }

            // Backtrack
            matches.RemoveAt(matches.Count - 1);
        }

        return false;
    }

    private static List<PlayerHistory> BuildHistories(Tournament tournament, List<Player> activePlayers)
    {
        var map = activePlayers.ToDictionary(p => p.Id, p => new PlayerHistory { Player = p });

        foreach (var round in tournament.Rounds.OrderBy(r => r.RoundNumber))
        {
            foreach (var pairing in round.Pairings)
            {
                if (pairing.Result == MatchResult.NotPlayed) continue;

                var (wPts, bPts) = pairing.Result.ToPoints();

                if (!string.IsNullOrEmpty(pairing.WhitePlayerId) && !string.IsNullOrEmpty(pairing.BlackPlayerId))
                {
                    if (map.TryGetValue(pairing.WhitePlayerId, out var wH) &&
                        map.TryGetValue(pairing.BlackPlayerId, out var bH))
                    {
                        wH.Points += wPts;
                        wH.Colors.Add('W');
                        wH.OpponentIds.Add(bH.Player.Id);

                        bH.Points += bPts;
                        bH.Colors.Add('B');
                        bH.OpponentIds.Add(wH.Player.Id);
                    }
                }
                else if (pairing.IsBye)
                {
                    string? byeId = pairing.WhitePlayerId ?? pairing.BlackPlayerId;
                    if (!string.IsNullOrEmpty(byeId) && map.TryGetValue(byeId, out var byeH))
                    {
                        double byePts = (pairing.Result == MatchResult.ByeFull) ? 1.0 :
                                        (pairing.Result == MatchResult.ByeHalf) ? 0.5 : 0.0;
                        byeH.Points += byePts;
                        if (pairing.Result == MatchResult.ByeFull)
                        {
                            byeH.ReceivedBye = true;
                        }
                    }
                }
            }
        }

        return map.Values.ToList();
    }
}
