using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Core.Settings;
using Caissa.Shared.Models;
using CaissaDesk.Data;

namespace CaissaDesk.Tests;

public class AppSettingsServiceTests
{
    [Fact]
    public void AppSettings_Defaults_AreValid()
    {
        var settings = new AppSettings();
        Assert.Equal("Tomas K.", settings.DefaultChiefArbiter);
        Assert.Equal("Chess Club", settings.DefaultVenue);
        Assert.Equal("SVK", settings.DefaultFederation);
        Assert.Equal(7, settings.DefaultTotalRounds);
        Assert.Equal(TournamentSystem.SwissDutch, settings.DefaultSystem);
        Assert.Equal(TimeControlCategory.Rapid, settings.DefaultTimeControl);
        Assert.Equal("15m + 5s", settings.DefaultTimeControlDetails);
        Assert.True(settings.DefaultFideRated);
        Assert.Equal("native_dutch", settings.DefaultSwissEngineId);
    }

    [Fact]
    public void AppSettings_Clone_CreatesIndependentCopy()
    {
        var original = new AppSettings
        {
            DefaultChiefArbiter = "Arbiter One",
            DefaultVenue = "Venue One",
            DefaultTotalRounds = 9,
            CustomBbpPath = "/usr/bin/bbp"
        };

        var clone = original.Clone();
        clone.DefaultChiefArbiter = "Arbiter Two";
        clone.DefaultTotalRounds = 11;

        Assert.Equal("Arbiter One", original.DefaultChiefArbiter);
        Assert.Equal(9, original.DefaultTotalRounds);
        Assert.Equal("Arbiter Two", clone.DefaultChiefArbiter);
        Assert.Equal(11, clone.DefaultTotalRounds);
        Assert.Equal("/usr/bin/bbp", clone.CustomBbpPath);
    }

    [Fact]
    public async Task AppSettingsService_SaveAndReload_PersistsChanges()
    {
        string tempConfigPath = Path.Combine(Path.GetTempPath(), $"desk_settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var registry = new PairingEngineRegistry();
            var service = new AppSettingsService(registry, tempConfigPath);

            bool eventFired = false;
            service.OnSettingsChanged += () => eventFired = true;

            var updated = service.Settings.Clone();
            updated.Language = "sk";
            updated.DefaultChiefArbiter = "IA Test Master";
            updated.DefaultVenue = "Congress Center";
            updated.DefaultFederation = "CZE";
            updated.DefaultTotalRounds = 9;
            updated.DefaultTimeControl = TimeControlCategory.Blitz;
            updated.DefaultTimeControlDetails = "3m + 2s";
            updated.DefaultSwissEngineId = "native_dutch";

            await service.SaveSettingsAsync(updated);

            Assert.True(eventFired);
            Assert.Equal("sk", service.Settings.Language);
            Assert.Equal("IA Test Master", service.Settings.DefaultChiefArbiter);
            Assert.Equal("Congress Center", service.Settings.DefaultVenue);
            Assert.Equal("CZE", service.Settings.DefaultFederation);
            Assert.Equal(9, service.Settings.DefaultTotalRounds);
            Assert.Equal(TimeControlCategory.Blitz, service.Settings.DefaultTimeControl);
            Assert.Equal("3m + 2s", service.Settings.DefaultTimeControlDetails);

            // Reload fresh from disk
            var service2 = new AppSettingsService(registry, tempConfigPath);
            Assert.Equal("sk", service2.Settings.Language);
            Assert.Equal("IA Test Master", service2.Settings.DefaultChiefArbiter);
            Assert.Equal("Congress Center", service2.Settings.DefaultVenue);
            Assert.Equal("CZE", service2.Settings.DefaultFederation);
            Assert.Equal(9, service2.Settings.DefaultTotalRounds);
            Assert.Equal(TimeControlCategory.Blitz, service2.Settings.DefaultTimeControl);
            Assert.Equal("3m + 2s", service2.Settings.DefaultTimeControlDetails);
        }
        finally
        {
            if (File.Exists(tempConfigPath)) File.Delete(tempConfigPath);
        }
    }

    [Fact]
    public void PairingEngineRegistry_TestEngine_NativeDutch_Passes()
    {
        var registry = new PairingEngineRegistry();
        var result = registry.TestEngine("native_dutch");

        Assert.True(result.Success);
        Assert.NotNull(result.Pairings);
        Assert.Equal(2, result.Pairings.Count); // 4 players -> 2 boards
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void PairingEngineRegistry_TestEngine_Unknown_FailsGracefully()
    {
        var registry = new PairingEngineRegistry();
        var result = registry.TestEngine("non_existent_engine");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PairingEngineRegistry_GetEngine_WhenTargetUnavailable_FallsBackToNativeDutch()
    {
        var registry = new PairingEngineRegistry();
        // Point bbp and javafo to non-existent paths
        registry.ReloadEngines(bbpPath: "/non_existent/bbp", javaPath: "/non_existent/java", javafoJarPath: "/non_existent/jar");

        // Requesting an unavailable engine falls back to native_dutch
        var engine = registry.GetEngine("bbp");
        if (!registry.AllEngines.First(e => e.Id == "bbp").IsAvailable)
        {
            Assert.Equal("native_dutch", engine.Id);
        }
    }

    [Fact]
    public async Task EngineInstallerService_UnsupportedEngine_ReturnsFail()
    {
        string tempConfigPath = Path.Combine(Path.GetTempPath(), $"desk_settings_test_{Guid.NewGuid():N}.json");
        try
        {
            using var client = new HttpClient();
            var registry = new PairingEngineRegistry();
            var settingsService = new AppSettingsService(registry, tempConfigPath);
            var installer = new EngineInstallerService(client, registry, settingsService);

            var result = await installer.InstallEngineAsync("unsupported_engine_xyz");
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempConfigPath)) File.Delete(tempConfigPath);
        }
    }

    [Fact]
    public async Task AppSettingsService_Save_LeavesNoTempFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"desk_settings_dir_{Guid.NewGuid():N}");
        string configPath = Path.Combine(dir, "desk_settings.json");
        try
        {
            var service = new AppSettingsService(new PairingEngineRegistry(), configPath);
            var updated = service.Settings.Clone();
            updated.DefaultVenue = "Atomic Hall";

            await service.SaveSettingsAsync(updated);
            updated.DefaultVenue = "Atomic Hall 2";
            await service.SaveSettingsAsync(updated);

            Assert.Equal(new[] { configPath }, Directory.GetFiles(dir));
            Assert.Equal("Atomic Hall 2", new AppSettingsService(new PairingEngineRegistry(), configPath).Settings.DefaultVenue);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AppSettingsService_SaveFailure_ThrowsAndKeepsStateUnchanged()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"desk_settings_dir_{Guid.NewGuid():N}");
        // A directory sitting at the config path makes the final replace fail
        string configPath = Path.Combine(dir, "desk_settings.json");
        Directory.CreateDirectory(configPath);
        try
        {
            var service = new AppSettingsService(new PairingEngineRegistry(), configPath);
            string originalVenue = service.Settings.DefaultVenue;

            bool eventFired = false;
            service.OnSettingsChanged += () => eventFired = true;

            var updated = service.Settings.Clone();
            updated.DefaultVenue = "Never Persisted";

            await Assert.ThrowsAnyAsync<Exception>(() => service.SaveSettingsAsync(updated));

            Assert.False(eventFired);
            Assert.Equal(originalVenue, service.Settings.DefaultVenue);
            Assert.Empty(Directory.GetFiles(dir)); // temp file cleaned up
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AppSettingsService_CorruptFile_IsBackedUpAndDefaultsUsed()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"desk_settings_dir_{Guid.NewGuid():N}");
        string configPath = Path.Combine(dir, "desk_settings.json");
        Directory.CreateDirectory(dir);
        File.WriteAllText(configPath, "{ \"DefaultVenue\": \"Half written");
        try
        {
            var service = new AppSettingsService(new PairingEngineRegistry(), configPath);

            Assert.Equal(new AppSettings().DefaultVenue, service.Settings.DefaultVenue);
            var backups = Directory.GetFiles(dir, "desk_settings.json.corrupt-*");
            Assert.Single(backups);
            Assert.Contains("Half written", File.ReadAllText(backups[0]));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
