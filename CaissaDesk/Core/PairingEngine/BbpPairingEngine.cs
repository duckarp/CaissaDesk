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

    public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber) =>
        GenerateRoundPairingsAsync(tournament, roundNumber).GetAwaiter().GetResult();

    public Task<PairingResult> GenerateRoundPairingsAsync(Tournament tournament, int roundNumber, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Task.FromResult(PairingResult.Fail("bbpPairings engine is not installed. Please install bbpPairings or place the binary in your PATH."));
        }

        return ExternalPairingEngineRunner.RunAsync("bbpPairings", tournament, roundNumber, ExecutablePath!,
            (trf, output) => ["--dutch", trf, "-p", output], cancellationToken: cancellationToken);
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
