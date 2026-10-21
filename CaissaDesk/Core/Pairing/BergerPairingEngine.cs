using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public class BergerPairingEngine : IPairingEngine
{
    public string Id => "berger";
    public string DisplayName => "Round Robin (FIDE Berger Tables)";
    public bool IsAvailable => true;

    public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber)
    {
        if (tournament.Players.Count < 2)
        {
            return PairingResult.Fail("At least 2 players are required to generate pairings.");
        }

        var players = tournament.Players.OrderBy(p => p.StartRank).ToList();
        int n = players.Count;
        int m = (n % 2 == 0) ? n : n + 1;
        int maxRounds = m - 1;

        if (roundNumber < 1 || roundNumber > maxRounds)
        {
            return PairingResult.Fail($"Round number {roundNumber} is out of range. Round Robin with {n} players has {maxRounds} rounds.");
        }

        var seedToPlayer = players.ToDictionary(p => p.StartRank, p => p);

        // Generate theoretical (whiteSeed, blackSeed) pairs for roundNumber
        var seedPairs = GenerateFideBergerRound(m, roundNumber);

        var pairings = new List<Pairing>();
        int boardNumber = 1;

        // Separate non-bye and bye pairings so byes appear on the last board
        var normalPairs = new List<(int White, int Black)>();
        var byePairs = new List<(int White, int Black)>();

        bool hasBye = (n % 2 != 0);
        foreach (var (whiteSeed, blackSeed) in seedPairs)
        {
            if (hasBye && (whiteSeed == m || blackSeed == m))
            {
                byePairs.Add((whiteSeed, blackSeed));
            }
            else
            {
                normalPairs.Add((whiteSeed, blackSeed));
            }
        }

        foreach (var (whiteSeed, blackSeed) in normalPairs)
        {
            var whitePlayer = seedToPlayer.GetValueOrDefault(whiteSeed);
            var blackPlayer = seedToPlayer.GetValueOrDefault(blackSeed);

            pairings.Add(new Pairing
            {
                Id = Guid.NewGuid().ToString("N"),
                RoundNumber = roundNumber,
                BoardNumber = boardNumber++,
                WhitePlayerId = whitePlayer?.Id,
                BlackPlayerId = blackPlayer?.Id,
                Result = MatchResult.NotPlayed
            });
        }

        foreach (var (whiteSeed, blackSeed) in byePairs)
        {
            // One of the seeds is m (the bye dummy player)
            if (whiteSeed == m)
            {
                // Black seed player gets the bye
                var blackPlayer = seedToPlayer.GetValueOrDefault(blackSeed);
                pairings.Add(new Pairing
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RoundNumber = roundNumber,
                    BoardNumber = boardNumber++,
                    WhitePlayerId = null,
                    BlackPlayerId = blackPlayer?.Id,
                    Result = MatchResult.ByeFull,
                    Remarks = "Bye"
                });
            }
            else
            {
                // White seed player gets the bye
                var whitePlayer = seedToPlayer.GetValueOrDefault(whiteSeed);
                pairings.Add(new Pairing
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RoundNumber = roundNumber,
                    BoardNumber = boardNumber++,
                    WhitePlayerId = whitePlayer?.Id,
                    BlackPlayerId = null,
                    Result = MatchResult.ByeFull,
                    Remarks = "Bye"
                });
            }
        }

        return PairingResult.Ok(pairings);
    }

    public List<Round> GenerateAllRounds(Tournament tournament)
    {
        var rounds = new List<Round>();
        int n = tournament.Players.Count;
        int m = (n % 2 == 0) ? n : n + 1;
        int totalRounds = m - 1;

        for (int r = 1; r <= totalRounds; r++)
        {
            var result = GenerateRoundPairings(tournament, r);
            if (result.Success)
            {
                rounds.Add(new Round
                {
                    RoundNumber = r,
                    Pairings = result.Pairings
                });
            }
        }

        return rounds;
    }

    /// <summary>
    /// Computes (whiteSeed, blackSeed) 1-based pairs for round r in an m-player tournament (m is even)
    /// strictly according to FIDE Handbook C.05.1 Berger tables.
    /// </summary>
    public static List<(int WhiteSeed, int BlackSeed)> GenerateFideBergerRound(int m, int r)
    {
        var pairs = new List<(int WhiteSeed, int BlackSeed)>();

        // 1. Partner of anchor player m
        int pM;
        if (r % 2 == 1)
        {
            pM = (r + 1) / 2;
            pairs.Add((pM, m)); // pM is White, m is Black
        }
        else
        {
            pM = (m / 2) + (r / 2);
            pairs.Add((m, pM)); // m is White, pM is Black
        }

        // 2. All other pairs: their sum modulo (m - 1) is constant: S = (r % (m - 1)) + 1
        int s = (r % (m - 1)) + 1;
        var used = new HashSet<int> { pM, m };

        for (int i = 1; i < m; i++)
        {
            if (used.Contains(i)) continue;

            int j = (s - i) % (m - 1);
            if (j <= 0) j += m - 1;

            if (j == i || used.Contains(j)) continue;

            used.Add(i);
            used.Add(j);

            int a = Math.Min(i, j);
            int b = Math.Max(i, j);

            // Official FIDE color rule:
            // If (a + b) is odd -> smaller seed (a) plays White
            // If (a + b) is even -> larger seed (b) plays White
            if ((a + b) % 2 == 1)
            {
                pairs.Add((a, b));
            }
            else
            {
                pairs.Add((b, a));
            }
        }

        return pairs;
    }
}
