using System.Diagnostics;
using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public class JaVaFoPairingEngine : IPairingEngine
{
    public string Id => "javafo";
    public string DisplayName => "JaVaFo (FIDE Reference Engine)";

    public string? JavaPath { get; private set; }
    public string? JarPath { get; private set; }

    public JaVaFoPairingEngine(string? customJavaPath = null, string? customJarPath = null)
    {
        SetCustomPaths(customJavaPath, customJarPath);
    }

    public void SetCustomPaths(string? customJavaPath, string? customJarPath)
    {
        JavaPath = !string.IsNullOrWhiteSpace(customJavaPath) && File.Exists(customJavaPath)
            ? customJavaPath
            : FindJavaExecutable();

        JarPath = !string.IsNullOrWhiteSpace(customJarPath) && File.Exists(customJarPath)
            ? customJarPath
            : FindJaVaFoJar();
    }

    public bool IsAvailable => !string.IsNullOrEmpty(JavaPath) && File.Exists(JavaPath) &&
                               !string.IsNullOrEmpty(JarPath) && File.Exists(JarPath);

    public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber)
    {
        if (!IsAvailable)
        {
            return PairingResult.Fail("JaVaFo pairing engine is not available. Please ensure Java JRE is installed and javafo.jar is located in the application directory.");
        }

        string tempTrf = Path.Combine(Path.GetTempPath(), $"caissadesk_in_{Guid.NewGuid():N}.trf");
        string tempOut = Path.Combine(Path.GetTempPath(), $"caissadesk_out_{Guid.NewGuid():N}.txt");

        try
        {
            // 1. Generate compliant FIDE TRF input
            string trfContent = TrfHandler.GenerateTrf(tournament);
            File.WriteAllText(tempTrf, trfContent);

            // 2. Invoke java -jar javafo.jar input.trf -p output.txt
            var psi = new ProcessStartInfo
            {
                FileName = JavaPath!,
                Arguments = $"-jar \"{JarPath}\" \"{tempTrf}\" -p \"{tempOut}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return PairingResult.Fail("Failed to start Java process for JaVaFo.");
            }

            process.WaitForExit(10000);
            if (process.ExitCode != 0)
            {
                string err = process.StandardError.ReadToEnd();
                return PairingResult.Fail($"JaVaFo exited with error code {process.ExitCode}: {err}");
            }

            if (!File.Exists(tempOut))
            {
                return PairingResult.Fail("JaVaFo did not produce a pairings output file.");
            }

            // 3. Parse pairings output
            string outputText = File.ReadAllText(tempOut);
            return ParsePairingsOutput(tournament, outputText, roundNumber);
        }
        catch (Exception ex)
        {
            return PairingResult.Fail($"JaVaFo execution failed: {ex.Message}");
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

                // Bye check
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

    private static string? FindJavaExecutable()
    {
        try
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
            string exeName = OperatingSystem.IsWindows() ? "java.exe" : "java";

            foreach (var p in paths)
            {
                var full = Path.Combine(p, exeName);
                if (File.Exists(full)) return full;
            }
        }
        catch { }

        return null;
    }

    private static string? FindJaVaFoJar()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = [
            Path.Combine(appData, "CaissaDesk", "engines", "javafo.jar"),
            Path.Combine(appData, "CaissaDesk", "javafo.jar"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engines", "javafo.jar"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "javafo.jar"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "javafo.jar"),
            Path.Combine(Directory.GetCurrentDirectory(), "javafo.jar")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}
