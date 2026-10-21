using System.Reflection;

namespace CaissaDesk.Core;

/// <summary>
/// Product version of the running build: the assembly informational version (set from &lt;Version&gt; in
/// Directory.Build.props, or -p:Version=... by the release workflow) without "+commit" build metadata.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(AppVersion).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return (plus >= 0 ? informational[..plus] : informational).Trim();
        }
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
