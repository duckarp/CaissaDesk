using System.Diagnostics;
using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

/// <summary>
/// Shared plumbing for external TRF-based pairing engines (bbpPairings, JaVaFo):
/// writes the TRF, runs the process safely and validates the pairings it returns.
/// </summary>
public static class ExternalPairingEngineRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <param name="buildArguments">Receives (trfPath, outputPath) and returns the argument list.</param>
    public static PairingResult Run(
        string engineName,
        Tournament tournament,
        int roundNumber,
        string fileName,
        Func<string, string, IEnumerable<string>> buildArguments,
        TimeSpan? timeout = null)
    {
        var precheck = ValidateInput(tournament, roundNumber);
        if (precheck != null) return precheck;

        // Nobody left to pair: only requested byes (or nothing) this round
        if (GetPlayersToPair(tournament, roundNumber).Count == 0)
        {
            var onlyByes = BuildRequestedByePairings(tournament, roundNumber, firstBoard: 1);
            return onlyByes.Count > 0
                ? PairingResult.Ok(onlyByes)
                : PairingResult.Fail("No active players available to pair.");
        }

        string tempTrf = Path.Combine(Path.GetTempPath(), $"caissadesk_in_{Guid.NewGuid():N}.trf");
        string tempOut = Path.Combine(Path.GetTempPath(), $"caissadesk_out_{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(tempTrf, TrfHandler.GenerateTrf(tournament, roundNumber));

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in buildArguments(tempTrf, tempOut))
            {
                psi.ArgumentList.Add(arg);
            }

            var run = RunProcess(psi, timeout ?? DefaultTimeout);
            if (run.Error != null)
            {
                return PairingResult.Fail($"{engineName}: {run.Error}");
            }

            if (run.ExitCode != 0)
            {
                string details = string.IsNullOrWhiteSpace(run.StdErr) ? run.StdOut : run.StdErr;
                return PairingResult.Fail($"{engineName} exited with code {run.ExitCode}: {details.Trim()}");
            }

            if (!File.Exists(tempOut))
            {
                return PairingResult.Fail($"{engineName} did not produce a pairings output file.");
            }

            return ParsePairingsOutput(engineName, tournament, File.ReadAllText(tempOut), roundNumber);
        }
        catch (Exception ex)
        {
            return PairingResult.Fail($"{engineName} execution failed: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempTrf)) File.Delete(tempTrf); } catch { }
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    public sealed record ProcessRun(int ExitCode, string StdOut, string StdErr, string? Error);

    /// <summary>
    /// Runs a process, draining stdout/stderr asynchronously (so a chatty engine cannot dead-lock on a
    /// full pipe) and killing the whole process tree if it does not finish within <paramref name="timeout"/>.
    /// </summary>
    public static ProcessRun RunProcess(ProcessStartInfo psi, TimeSpan timeout)
    {
        using var process = Process.Start(psi);
        if (process == null)
        {
            return new ProcessRun(-1, "", "", $"failed to start process '{psi.FileName}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { process.WaitForExit(5000); } catch { }
            return new ProcessRun(-1, "", "", $"timed out after {timeout.TotalSeconds:0} s and was terminated.");
        }

        // Ensures redirected streams are fully read once the process has exited
        process.WaitForExit();
        return new ProcessRun(process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult(), null);
    }

    /// <summary>Active players without an advance bye request for the round, i.e. the engine's pool.</summary>
    public static List<Player> GetPlayersToPair(Tournament tournament, int roundNumber) =>
        tournament.Players
            .Where(p => p.Status == PlayerStatus.Active &&
                        !(p.ByeRequests.TryGetValue(roundNumber, out var bye) && bye != ByeType.None))
            .OrderBy(p => p.StartRank)
            .ToList();

    public static List<Pairing> BuildRequestedByePairings(Tournament tournament, int roundNumber, int firstBoard)
    {
        int board = firstBoard;
        var result = new List<Pairing>();
        foreach (var p in tournament.Players.Where(p => p.Status == PlayerStatus.Active).OrderBy(p => p.StartRank))
        {
            if (!p.ByeRequests.TryGetValue(roundNumber, out var byeType) || byeType == ByeType.None) continue;

            var byeResult = byeType.ToMatchResult();
            result.Add(new Pairing
            {
                Id = Guid.NewGuid().ToString("N"),
                RoundNumber = roundNumber,
                BoardNumber = board++,
                WhitePlayerId = p.Id,
                BlackPlayerId = null,
                Result = byeResult,
                Remarks = byeResult switch
                {
                    MatchResult.ByeHalf => "½-Point Bye (Requested)",
                    MatchResult.ByeFullRequested => "1-Point Bye (Requested)",
                    _ => "0-Point Bye (Requested)"
                }
            });
        }
        return result;
    }

    private static PairingResult? ValidateInput(Tournament tournament, int roundNumber)
    {
        var duplicateRank = tournament.Players.GroupBy(p => p.StartRank).FirstOrDefault(g => g.Count() > 1);
        if (duplicateRank != null)
        {
            return PairingResult.Fail($"Starting rank {duplicateRank.Key} is assigned to more than one player. Renumber the starting ranks before pairing.");
        }

        if (tournament.Players.Any(p => p.StartRank <= 0))
        {
            return PairingResult.Fail("Every player needs a positive starting rank before pairing.");
        }

        foreach (var r in tournament.Rounds.Where(r => r.RoundNumber < roundNumber).OrderBy(r => r.RoundNumber))
        {
            int uncompleted = r.Pairings.Count(p => p.Result == MatchResult.NotPlayed);
            if (uncompleted > 0)
            {
                return PairingResult.Fail($"Cannot pair Round {roundNumber}: Round {r.RoundNumber} still has {uncompleted} game(s) with results pending.");
            }
        }

        return null;
    }

    /// <summary>
    /// Parses "white black" seed pairs (bbpPairings/JaVaFo -p output; 0 = bye) and checks that every player
    /// to be paired appears exactly once and nobody else does.
    /// </summary>
    public static PairingResult ParsePairingsOutput(string engineName, Tournament tournament, string outputText, int roundNumber)
    {
        var seedToPlayer = tournament.Players.ToDictionary(p => p.StartRank, p => p);
        var expected = GetPlayersToPair(tournament, roundNumber).Select(p => p.Id).ToHashSet();
        var seen = new HashSet<string>();

        var games = new List<(Player White, Player Black)>();
        Player? byePlayer = null;
        int? declaredPairs = null;

        var lines = outputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            if (parts.Length == 1)
            {
                // First line: number of pairs
                if (declaredPairs == null && games.Count == 0 && int.TryParse(parts[0], out int n)) declaredPairs = n;
                continue;
            }

            if (!int.TryParse(parts[0], out int whiteSeed) || !int.TryParse(parts[1], out int blackSeed))
            {
                return PairingResult.Fail($"{engineName} returned an unreadable pairing line: \"{line.Trim()}\".");
            }

            if (whiteSeed == 0 && blackSeed == 0) continue;

            Player? Resolve(int seed, out string? error)
            {
                error = null;
                if (!seedToPlayer.TryGetValue(seed, out var player))
                {
                    error = $"{engineName} returned unknown starting rank {seed}.";
                    return null;
                }
                if (!expected.Contains(player.Id))
                {
                    error = $"{engineName} paired {player.FullName} (#{seed}), who is not available for Round {roundNumber}.";
                    return null;
                }
                if (!seen.Add(player.Id))
                {
                    error = $"{engineName} paired {player.FullName} (#{seed}) more than once.";
                    return null;
                }
                return player;
            }

            if (whiteSeed == 0 || blackSeed == 0)
            {
                var p = Resolve(whiteSeed == 0 ? blackSeed : whiteSeed, out var err);
                if (p == null) return PairingResult.Fail(err!);
                if (byePlayer != null) return PairingResult.Fail($"{engineName} allocated more than one pairing bye.");
                byePlayer = p;
            }
            else
            {
                var w = Resolve(whiteSeed, out var errW);
                if (w == null) return PairingResult.Fail(errW!);
                var b = Resolve(blackSeed, out var errB);
                if (b == null) return PairingResult.Fail(errB!);
                games.Add((w, b));
            }
        }

        var missing = expected.Except(seen).ToList();
        if (missing.Count > 0)
        {
            var names = missing.Select(id => tournament.GetPlayer(id)?.FullName ?? id);
            return PairingResult.Fail($"{engineName} did not pair: {string.Join(", ", names)}.");
        }

        if (declaredPairs != null && declaredPairs != games.Count + (byePlayer != null ? 1 : 0))
        {
            return PairingResult.Fail($"{engineName} output declares {declaredPairs} pairs but lists {games.Count + (byePlayer != null ? 1 : 0)}.");
        }

        var pairings = new List<Pairing>();
        int board = 1;
        foreach (var (white, black) in games)
        {
            pairings.Add(new Pairing
            {
                Id = Guid.NewGuid().ToString("N"),
                RoundNumber = roundNumber,
                BoardNumber = board++,
                WhitePlayerId = white.Id,
                BlackPlayerId = black.Id,
                Result = MatchResult.NotPlayed
            });
        }

        if (byePlayer != null)
        {
            pairings.Add(new Pairing
            {
                Id = Guid.NewGuid().ToString("N"),
                RoundNumber = roundNumber,
                BoardNumber = board++,
                WhitePlayerId = byePlayer.Id,
                BlackPlayerId = null,
                Result = MatchResult.ByeFull,
                Remarks = "1-Point Bye"
            });
        }

        pairings.AddRange(BuildRequestedByePairings(tournament, roundNumber, board));
        return PairingResult.Ok(pairings);
    }
}
