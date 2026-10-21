using System.Diagnostics;
using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public class BbpPairingEngine : IPairingEngine
{
    public string Id => "bbp";
    public string DisplayName => "bbpPairings (FIDE Certified C++)";

    public string? ExecutablePath { get; private set; }

    public BbpPairingEngine(string? customPath = null)
    {
        SetCustomPath(customPath);
    }

    public void SetCustomPath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            ExecutablePath = customPath;
        }
        else
        {
            ExecutablePath = FindBbpExecutable();
        }
    }

    public bool IsAvailable => !string.IsNullOrEmpty(ExecutablePath) && File.Exists(ExecutablePath);

    public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber)
    {
        if (!IsAvailable)
        {
            return PairingResult.Fail("bbpPairings engine is not installed. Please install bbpPairings or place the binary in your PATH.");
        }

        string tempTrf = Path.Combine(Path.GetTempPath(), $"caissadesk_bbp_{Guid.NewGuid():N}.trf");
        string tempOut = Path.Combine(Path.GetTempPath(), $"caissadesk_bbp_{Guid.NewGuid():N}.txt");

        try
        {
            string trfContent = TrfHandler.GenerateTrf(tournament);
            File.WriteAllText(tempTrf, trfContent);

            var psi = new ProcessStartInfo
            {
                FileName = ExecutablePath!,
                Arguments = $"--dutch \"{tempTrf}\" -p \"{tempOut}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return PairingResult.Fail("Failed to start bbpPairings process.");
            }

            process.WaitForExit(10000);
            if (process.ExitCode != 0)
            {
                string err = process.StandardError.ReadToEnd();
                return PairingResult.Fail($"bbpPairings exited with code {process.ExitCode}: {err}");
            }

            if (!File.Exists(tempOut))
            {
                return PairingResult.Fail("bbpPairings did not produce an output file.");
            }

            string outputText = File.ReadAllText(tempOut);
            return ParsePairingsOutput(tournament, outputText, roundNumber);
        }
        catch (Exception ex)
        {
            return PairingResult.Fail($"bbpPairings execution failed: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempTrf)) File.Delete(tempTrf); } catch { }
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    private static PairingResult ParsePairingsOutput(Tournament tournament, string outputText, int roundNumber)
    {
        var seedToPlayer = tournament.Players.ToDictionary(p => p.StartRank, p => p);
        var pairings = new List<Pairing>();
        int boardNumber = 1;

        var lines = outputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (int.TryParse(parts[0], out int whiteSeed) && int.TryParse(parts[1], out int blackSeed))
            {
                if (whiteSeed == 0 && blackSeed == 0) continue;

                if (blackSeed == 0)
                {
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
                else if (whiteSeed == 0)
                {
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
                    var white = seedToPlayer.GetValueOrDefault(whiteSeed);
                    var black = seedToPlayer.GetValueOrDefault(blackSeed);

                    pairings.Add(new Pairing
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RoundNumber = roundNumber,
                        BoardNumber = boardNumber++,
                        WhitePlayerId = white?.Id,
                        BlackPlayerId = black?.Id,
                        Result = MatchResult.NotPlayed
                    });
                }
            }
        }

        return PairingResult.Ok(pairings);
    }

    private static string? FindBbpExecutable()
    {
        try
        {
            string exeName = OperatingSystem.IsWindows() ? "bbpPairings.exe" : "bbpPairings";
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] directCandidates = [
                Path.Combine(appData, "CaissaDesk", "engines", exeName),
                Path.Combine(appData, "CaissaDesk", "engines", "bbpPairings"),
                Path.Combine(appData, "CaissaDesk", "engines", "bbpPairings.exe"),
                Path.Combine(appData, "CaissaDesk", exeName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engines", exeName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName)
            ];

            foreach (var cand in directCandidates)
            {
                if (File.Exists(cand)) return cand;
            }

            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
            foreach (var p in paths)
            {
                var full = Path.Combine(p, exeName);
                if (File.Exists(full)) return full;
            }
        }
        catch { }

        return null;
    }
}
