using Caissa.Shared.Models;
using CaissaDesk.Core;

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

    public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber) =>
        GenerateRoundPairingsAsync(tournament, roundNumber).GetAwaiter().GetResult();

    public Task<PairingResult> GenerateRoundPairingsAsync(Tournament tournament, int roundNumber, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Task.FromResult(PairingResult.Fail("JaVaFo pairing engine is not available. Please ensure Java JRE is installed and javafo.jar is located in the application directory."));
        }

        string jarPath = JarPath!;
        return ExternalPairingEngineRunner.RunAsync("JaVaFo", tournament, roundNumber, JavaPath!,
            (trf, output) => ["-jar", jarPath, trf, "-p", output], cancellationToken: cancellationToken);
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
        string appData = AppPaths.LocalAppDataRoot;
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
