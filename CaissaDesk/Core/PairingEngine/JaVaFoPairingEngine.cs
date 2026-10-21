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

        return ExternalPairingEngineRunner.Run("JaVaFo", tournament, roundNumber, JavaPath!, (trf, output) => ["-jar", JarPath!, trf, "-p", output]);
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
