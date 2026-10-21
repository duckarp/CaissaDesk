using System.Runtime.InteropServices;
using Caissa.Shared.Tiebreak;
using CaissaDesk.Components;
using CaissaDesk.Core.Arbiter;
using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Data;
using CaissaDesk.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Photino.Blazor;

namespace CaissaDesk;

internal class Program
{
    [DllImport("libglib-2.0.so.0", EntryPoint = "g_set_prgname", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_set_prgname(string prgname);

    [STAThread]
    static void Main(string[] args)
    {
        SetLinuxProgramName("caissadesk");

        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        // Register application services
        appBuilder.Services.AddLogging();
        appBuilder.Services.AddHttpClient();
        appBuilder.Services.AddSingleton<IAppWindowProvider, AppWindowProvider>();
        appBuilder.Services.AddSingleton<ITiebreakService, TiebreakService>();
        appBuilder.Services.AddSingleton<ITournamentRepository, TournamentRepository>();
        appBuilder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
        appBuilder.Services.AddSingleton<IPairingEngineRegistry, PairingEngineRegistry>();
        appBuilder.Services.AddSingleton<IEngineInstallerService, EngineInstallerService>();
        appBuilder.Services.AddSingleton<IArbiterService, ArbiterService>();
        appBuilder.Services.AddSingleton<IFileExportService, FileExportService>();
        appBuilder.Services.AddSingleton<IFideScoutingService, FideScoutingService>();
        appBuilder.Services.AddSingleton<INationalScoutingService, NationalScoutingService>();
        appBuilder.Services.AddSingleton<ICaissaResultsSyncService, CaissaResultsSyncService>();
        appBuilder.Services.AddSingleton<IChessResultsScoutingService, ChessResultsScoutingService>();
        appBuilder.Services.AddScoped<WorkspaceState>();
        appBuilder.Services.AddScoped<LocalizationService>(sp =>
            new LocalizationService(sp.GetRequiredService<IAppSettingsService>()));
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
        catch
        {
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
        var iconFile = EnsureIconOnDisk(fileProvider);

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
                Console.Error.WriteLine($"[CaissaDesk] Could not set window icon: {ex.Message}");
            }
        }

        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            Console.Error.WriteLine($"[CaissaDesk] Unhandled exception: {error.ExceptionObject}");
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                EnsureLinuxDesktopIntegration(fileProvider);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CaissaDesk] Linux desktop integration error: {ex}");
            }
        }

        app.Run();
    }

    private static void SetLinuxProgramName(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                g_set_prgname(name);
            }
            catch
            {
                // Non-fatal if libglib is not available
            }
        }
    }

    private static string? EnsureIconOnDisk(IFileProvider fileProvider)
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
            Console.Error.WriteLine($"[CaissaDesk] Could not extract icon to disk: {ex.Message}");
        }

        return null;
    }

    private static void EnsureLinuxDesktopIntegration(IFileProvider fileProvider)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) return;

            var appsDir = Path.Combine(home, ".local", "share", "applications");
            var icon256Dir = Path.Combine(home, ".local", "share", "icons", "hicolor", "256x256", "apps");
            var iconSvgDir = Path.Combine(home, ".local", "share", "icons", "hicolor", "scalable", "apps");

            Directory.CreateDirectory(appsDir);
            Directory.CreateDirectory(icon256Dir);
            Directory.CreateDirectory(iconSvgDir);

            // Clean up any low-res raster icons that might cause desktop environments to render tight-padded icons
            string[] cleanupSizes = ["16x16", "24x24", "32x32", "48x48", "64x64", "128x128"];
            foreach (var size in cleanupSizes)
            {
                var sizeApps = Path.Combine(home, ".local", "share", "icons", "hicolor", size, "apps");
                foreach (var fname in new[] { "caissadesk.png", "CaissaDesk.png" })
                {
                    var file = Path.Combine(sizeApps, fname);
                    if (File.Exists(file))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }

            // Copy/extract 256x256 icon
            var targetPng = Path.Combine(icon256Dir, "caissadesk.png");
            var pngInfo = fileProvider.GetFileInfo("icon-256.png");
            if (!pngInfo.Exists) pngInfo = fileProvider.GetFileInfo("icon.png");
            if (!pngInfo.Exists) pngInfo = fileProvider.GetFileInfo("favicon.png");

            if (pngInfo.Exists)
            {
                using var src = pngInfo.CreateReadStream();
                using var dst = File.Create(targetPng);
                src.CopyTo(dst);
                File.Copy(targetPng, Path.Combine(icon256Dir, "CaissaDesk.png"), true);
            }

            // Copy/extract scalable SVG icon
            var targetSvg = Path.Combine(iconSvgDir, "caissadesk.svg");
            var svgInfo = fileProvider.GetFileInfo("images/icon.svg");
            if (!svgInfo.Exists) svgInfo = fileProvider.GetFileInfo("icon.svg");
            if (!svgInfo.Exists) svgInfo = fileProvider.GetFileInfo("favicon.svg");

            if (svgInfo.Exists)
            {
                using var src = svgInfo.CreateReadStream();
                using var dst = File.Create(targetSvg);
                src.CopyTo(dst);
                File.Copy(targetSvg, Path.Combine(iconSvgDir, "CaissaDesk.svg"), true);
            }

            var desktopFile = Path.Combine(appsDir, "caissadesk.desktop");
            var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CaissaDesk");
            var desktopContent = $"""
                [Desktop Entry]
                Name=CaissaDesk
                GenericName=Chess Tournament Management
                Comment=Chess Tournament Workstation
                Exec="{exePath}"
                Icon=caissadesk
                Terminal=false
                Type=Application
                Categories=Game;BoardGame;Utility;
                StartupWMClass=caissadesk
                StartupNotify=true
                """;
            File.WriteAllText(desktopFile, desktopContent);
            File.WriteAllText(Path.Combine(appsDir, "CaissaDesk.desktop"), desktopContent);
        }
        catch
        {
            // Non-fatal desktop entry creation
        }
    }
}
