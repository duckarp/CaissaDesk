using System.Runtime.InteropServices;
using Caissa.Shared.Tiebreak;
using CaissaDesk.Components;
using CaissaDesk.Core;
using CaissaDesk.Core.Notifications;
using CaissaDesk.Core.Arbiter;
using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Core.Roster;
using CaissaDesk.Core.Updates;
using CaissaDesk.Data;
using CaissaDesk.Localization;
using CaissaDesk.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Photino.Blazor;

namespace CaissaDesk;

internal class Program
{
    [DllImport("libglib-2.0.so.0", EntryPoint = "g_set_prgname", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_set_prgname(string prgname);

    /// <summary>Bump when the Linux desktop integration layout changes and old artefacts need cleaning up.</summary>
    private const int DesktopIntegrationVersion = 2;

    [STAThread]
    static void Main(string[] args)
    {
        // WinExe has no console: route all diagnostics to a rolling log file.
        var fileLoggerProvider = new FileLoggerProvider();
        var log = fileLoggerProvider.CreateLogger("CaissaDesk.Program");

        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
        {
            log.LogCritical(error.ExceptionObject as Exception,
                "Unhandled exception (terminating: {IsTerminating}): {ExceptionObject}",
                error.IsTerminating, error.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, error) =>
        {
            log.LogError(error.Exception, "Unobserved task exception.");
            error.SetObserved();
        };

        log.LogInformation("CaissaDesk {Version} starting on {OS} (.NET {Runtime}).",
            AppVersion.Current, RuntimeInformation.OSDescription, Environment.Version);

        SetLinuxProgramName("caissadesk", log);

        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        // Register application services
        appBuilder.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(fileLoggerProvider);
        });
        appBuilder.Services.AddHttpClient();
        appBuilder.Services.AddScoutingHttpClients();
        UpdateService.AddUpdateHttpClient(appBuilder.Services);
        appBuilder.Services.AddSingleton<IAppWindowProvider, AppWindowProvider>();
        appBuilder.Services.AddSingleton<ITiebreakService, TiebreakService>();
        appBuilder.Services.AddSingleton<ITournamentRepository>(sp =>
            new TournamentRepository(null, sp.GetRequiredService<ILogger<TournamentRepository>>()));
        appBuilder.Services.AddSingleton<IAppSettingsService>(sp =>
            new AppSettingsService(
                sp.GetRequiredService<IPairingEngineRegistry>(),
                null,
                sp.GetRequiredService<ILogger<AppSettingsService>>()));
        appBuilder.Services.AddSingleton<IPairingEngineRegistry, PairingEngineRegistry>();
        appBuilder.Services.AddSingleton<IEngineInstallerService, EngineInstallerService>();
        appBuilder.Services.AddSingleton<IArbiterService, ArbiterService>();
        appBuilder.Services.AddSingleton<IFileExportService, FileExportService>();
        appBuilder.Services.AddSingleton<IFideScoutingService, FideScoutingService>();
        appBuilder.Services.AddSingleton<INationalScoutingService, NationalScoutingService>();
        appBuilder.Services.AddSingleton<ICaissaResultsSyncService, CaissaResultsSyncService>();
        appBuilder.Services.AddSingleton<IChessResultsScoutingService, ChessResultsScoutingService>();
        appBuilder.Services.AddSingleton<IRoundGenerationService, RoundGenerationService>();
        appBuilder.Services.AddSingleton<IRosterService, RosterService>();
        appBuilder.Services.AddSingleton<IPlayerLookupService, PlayerLookupService>();
        appBuilder.Services.AddSingleton<INotificationService, NotificationService>();
        appBuilder.Services.AddSingleton<IExternalLinkService, ExternalLinkService>();
        appBuilder.Services.AddSingleton<IUpdateService>(sp =>
            new UpdateService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IAppSettingsService>(),
                sp.GetRequiredService<ILogger<UpdateService>>(),
                sp.GetRequiredService<IAppWindowProvider>()));
        appBuilder.Services.AddScoped<WorkspaceState>();
        appBuilder.Services.AddScoped<LocalizationService>(sp =>
            new LocalizationService(sp.GetRequiredService<IAppSettingsService>(), logger: sp.GetRequiredService<ILogger<LocalizationService>>()));
        appBuilder.Services.AddScoped<ILocalizationService>(sp => sp.GetRequiredService<LocalizationService>());

        // Configure file provider supporting both physical wwwroot and embedded resources
        var physicalWwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
        IFileProvider fileProvider;
        try
        {
            var embeddedProvider = new ManifestEmbeddedFileProvider(typeof(App).Assembly, "wwwroot");
            if (Directory.Exists(physicalWwwroot))
            {
                fileProvider = new CompositeFileProvider(
                    new PhysicalFileProvider(physicalWwwroot),
                    embeddedProvider);
            }
            else
            {
                fileProvider = embeddedProvider;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Embedded wwwroot unavailable; falling back to physical files at {Path}.", physicalWwwroot);
            if (!Directory.Exists(physicalWwwroot))
            {
                Directory.CreateDirectory(physicalWwwroot);
            }
            fileProvider = new PhysicalFileProvider(physicalWwwroot);
        }

        var existingProviderDescriptor = appBuilder.Services.FirstOrDefault(s => s.ServiceType == typeof(IFileProvider));
        if (existingProviderDescriptor != null)
        {
            appBuilder.Services.Remove(existingProviderDescriptor);
        }
        appBuilder.Services.AddSingleton<IFileProvider>(fileProvider);

        // Register root desktop component
        appBuilder.RootComponents.Add<App>("#app");

        var app = appBuilder.Build();

        // Expose the native window to Blazor components
        app.Services.GetRequiredService<IAppWindowProvider>().Window = app.MainWindow;

        // Configure native desktop window
        var iconFile = EnsureIconOnDisk(fileProvider, log);

        app.MainWindow
            .SetTitle("CaissaDesk")
            .SetSize(1400, 900)
            .SetMinSize(1000, 650)
            .SetMediaAutoplayEnabled(true)
            .SetUseOsDefaultLocation(false)
#if DEBUG
            .SetDevToolsEnabled(true);
#else
            .SetDevToolsEnabled(false);
#endif

        if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
        {
            try
            {
                app.MainWindow.SetIconFile(iconFile);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Could not set window icon {IconFile}.", iconFile);
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                EnsureLinuxDesktopIntegration(fileProvider, log);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Linux desktop integration failed.");
            }
        }

        StartBackgroundUpdateCheck(app.Services.GetRequiredService<IUpdateService>(), log);

        try
        {
            app.Run();
            log.LogInformation("CaissaDesk exited normally.");
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "CaissaDesk terminated with an unhandled exception.");
            throw;
        }
        finally
        {
            fileLoggerProvider.Dispose();
        }
    }

    /// <summary>
    /// Checks GitHub Releases shortly after startup, off the UI thread, when "check on startup" is enabled
    /// and the last check is old enough (see UpdateService.ShouldAutoCheck). Failures are only logged.
    /// </summary>
    private static void StartBackgroundUpdateCheck(IUpdateService updateService, ILogger log)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                await updateService.CheckIfDueAsync();
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Background update check failed.");
            }
        });
    }

    private static void SetLinuxProgramName(string name, ILogger log)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                g_set_prgname(name);
            }
            catch (Exception ex)
            {
                // Non-fatal if libglib is not available
                log.LogDebug(ex, "g_set_prgname unavailable.");
            }
        }
    }

    private static string? EnsureIconOnDisk(IFileProvider fileProvider, ILogger log)
    {
        try
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var targetDir = !string.IsNullOrEmpty(appData)
                ? Path.Combine(appData, "CaissaDesk")
                : Path.Combine(Path.GetTempPath(), "caissadesk");

            Directory.CreateDirectory(targetDir);

            if (isWindows)
            {
                string[] winPhysicalCandidates = [
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "icon.ico"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "favicon.ico"),
                    Path.Combine(Directory.GetCurrentDirectory(), "icon.ico"),
                    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "icon.ico"),
                    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "favicon.ico"),
                ];

                foreach (var c in winPhysicalCandidates)
                {
                    if (File.Exists(c) && new FileInfo(c).Length > 0)
                    {
                        return c;
                    }
                }

                var targetIco = Path.Combine(targetDir, "icon.ico");
                string[] embeddedIcoNames = ["icon.ico", "favicon.ico"];
                foreach (var name in embeddedIcoNames)
                {
                    var fileInfo = fileProvider.GetFileInfo(name);
                    if (fileInfo.Exists)
                    {
                        if (!File.Exists(targetIco) || new FileInfo(targetIco).Length != fileInfo.Length)
                        {
                            using var src = fileInfo.CreateReadStream();
                            using var dst = File.Create(targetIco);
                            src.CopyTo(dst);
                        }
                        return targetIco;
                    }
                }
            }

            // Fallback for non-Windows (Linux/macOS)
            string[] physicalCandidates = [
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "icon-256.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "icon.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "favicon.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "icon-256.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "favicon.png"),
            ];

            foreach (var c in physicalCandidates)
            {
                if (File.Exists(c) && new FileInfo(c).Length > 0)
                {
                    return c;
                }
            }

            var targetPng = Path.Combine(targetDir, "icon-256.png");
            string[] embeddedNames = ["icon-256.png", "icon.png", "favicon.png"];
            foreach (var name in embeddedNames)
            {
                var fileInfo = fileProvider.GetFileInfo(name);
                if (fileInfo.Exists)
                {
                    if (!File.Exists(targetPng) || new FileInfo(targetPng).Length != fileInfo.Length)
                    {
                        using var src = fileInfo.CreateReadStream();
                        using var dst = File.Create(targetPng);
                        src.CopyTo(dst);
                    }
                    return targetPng;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not extract icon to disk.");
        }

        return null;
    }

    private static void EnsureLinuxDesktopIntegration(IFileProvider fileProvider, ILogger log)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return;

        var shareDir = Path.Combine(home, ".local", "share");
        var appsDir = Path.Combine(shareDir, "applications");
        var iconsRoot = Path.Combine(shareDir, "icons", "hicolor");
        var icon256Dir = Path.Combine(iconsRoot, "256x256", "apps");
        var iconSvgDir = Path.Combine(iconsRoot, "scalable", "apps");

        Directory.CreateDirectory(appsDir);
        Directory.CreateDirectory(icon256Dir);
        Directory.CreateDirectory(iconSvgDir);

        // One-time cleanup of artefacts written by older builds, gated by a version marker
        // so we do not touch the icon theme on every launch.
        var markerDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaissaDesk");
        var markerFile = Path.Combine(markerDir, "desktop-integration.version");
        if (ReadMarkerVersion(markerFile, log) < DesktopIntegrationVersion)
        {
            // Low-res raster icons made desktop environments render tight-padded icons.
            string[] cleanupSizes = ["16x16", "24x24", "32x32", "48x48", "64x64", "128x128"];
            foreach (var size in cleanupSizes)
            {
                foreach (var fname in new[] { "caissadesk.png", "CaissaDesk.png" })
                {
                    TryDelete(Path.Combine(iconsRoot, size, "apps", fname), log);
                }
            }

            // Mixed-case duplicates of our icons.
            TryDelete(Path.Combine(icon256Dir, "CaissaDesk.png"), log);
            TryDelete(Path.Combine(iconSvgDir, "CaissaDesk.svg"), log);

            try
            {
                Directory.CreateDirectory(markerDir);
                File.WriteAllText(markerFile, DesktopIntegrationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                log.LogInformation("Cleaned up legacy desktop integration files (marker v{Version}).", DesktopIntegrationVersion);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Could not write desktop integration marker {Path}.", markerFile);
            }
        }

        // Stale duplicate desktop entry created by older builds; only remove it if it is ours.
        var staleDesktopFile = Path.Combine(appsDir, "CaissaDesk.desktop");
        try
        {
            if (File.Exists(staleDesktopFile) &&
                File.ReadAllText(staleDesktopFile).Contains("StartupWMClass=caissadesk", StringComparison.Ordinal))
            {
                File.Delete(staleDesktopFile);
                log.LogInformation("Removed stale desktop entry {Path}.", staleDesktopFile);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not remove stale desktop entry {Path}.", staleDesktopFile);
        }

        // 256x256 raster icon
        var pngInfo = fileProvider.GetFileInfo("icon-256.png");
        if (!pngInfo.Exists) pngInfo = fileProvider.GetFileInfo("icon.png");
        if (!pngInfo.Exists) pngInfo = fileProvider.GetFileInfo("favicon.png");
        if (pngInfo.Exists)
        {
            WriteIfChanged(Path.Combine(icon256Dir, "caissadesk.png"), ReadAll(pngInfo), log);
        }

        // Scalable SVG icon
        var svgInfo = fileProvider.GetFileInfo("images/icon.svg");
        if (!svgInfo.Exists) svgInfo = fileProvider.GetFileInfo("icon.svg");
        if (!svgInfo.Exists) svgInfo = fileProvider.GetFileInfo("favicon.svg");
        if (svgInfo.Exists)
        {
            WriteIfChanged(Path.Combine(iconSvgDir, "caissadesk.svg"), ReadAll(svgInfo), log);
        }

        // Prefer the AppImage path when running from an AppImage: ProcessPath points into the
        // temporary squashfs mount, which disappears when the app exits.
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        var exePath = !string.IsNullOrWhiteSpace(appImage) && File.Exists(appImage)
            ? appImage
            : Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CaissaDesk");

        var desktopContent = $"""
            [Desktop Entry]
            Name=CaissaDesk
            GenericName=Chess Tournament Management
            Comment=Chess Tournament Workstation
            Exec="{EscapeDesktopExecArgument(exePath)}"
            Icon=caissadesk
            Terminal=false
            Type=Application
            Categories=Game;BoardGame;Utility;
            StartupWMClass=caissadesk
            StartupNotify=true

            """;
        WriteIfChanged(Path.Combine(appsDir, "caissadesk.desktop"), System.Text.Encoding.UTF8.GetBytes(desktopContent), log);
    }

    private static int ReadMarkerVersion(string markerFile, ILogger log)
    {
        try
        {
            if (File.Exists(markerFile) &&
                int.TryParse(File.ReadAllText(markerFile).Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int v))
            {
                return v;
            }
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Could not read desktop integration marker {Path}.", markerFile);
        }
        return 0;
    }

    private static byte[] ReadAll(IFileInfo fileInfo)
    {
        using var src = fileInfo.CreateReadStream();
        using var ms = new MemoryStream();
        src.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteIfChanged(string path, byte[] content, ILogger log)
    {
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (info.Length == content.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
                {
                    return;
                }
            }

            File.WriteAllBytes(path, content);
            log.LogInformation("Updated desktop integration file {Path}.", path);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not write desktop integration file {Path}.", path);
        }
    }

    private static void TryDelete(string path, ILogger log)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Could not delete {Path}.", path);
        }
    }

    /// <summary>
    /// Escapes a value for use inside a double-quoted Exec argument of a .desktop file
    /// (quoting rules applied first, then the general string escape for backslashes, plus % field codes).
    /// </summary>
    private static string EscapeDesktopExecArgument(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\\\"); break;
                case '"': sb.Append(@"\\"""); break;
                case '`': sb.Append(@"\\`"); break;
                case '$': sb.Append(@"\\$"); break;
                case '%': sb.Append("%%"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
