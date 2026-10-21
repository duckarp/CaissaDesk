using System.Text.Json;
using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Core.Settings;

namespace CaissaDesk.Data;

public interface IAppSettingsService
{
    AppSettings Settings { get; }
    Task LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings newSettings);
    event Action? OnSettingsChanged;
}

public class AppSettingsService : IAppSettingsService
{
    private readonly IPairingEngineRegistry _engineRegistry;
    private readonly string? _customConfigPath;
    private AppSettings _settings = new();

    public AppSettings Settings => _settings;
    public event Action? OnSettingsChanged;

    public AppSettingsService(IPairingEngineRegistry engineRegistry, string? customConfigPath = null)
    {
        _engineRegistry = engineRegistry;
        _customConfigPath = customConfigPath;
        LoadSettingsSync();
    }

    private string GetConfigFilePath()
    {
        if (!string.IsNullOrEmpty(_customConfigPath))
        {
            var dir = Path.GetDirectoryName(_customConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            return _customConfigPath;
        }

        string configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaissaDesk");
        Directory.CreateDirectory(configDir);
        return Path.Combine(configDir, "desk_settings.json");
    }

    private void LoadSettingsSync()
    {
        string path;
        try
        {
            path = GetConfigFilePath();
            if (!File.Exists(path)) return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error locating settings file: {ex.Message}");
            return;
        }

        AppSettings? loaded;
        try
        {
            string json = File.ReadAllText(path);
            loaded = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            // Keep the unreadable file so the user's settings can be recovered, then fall back to defaults
            BackUpCorruptFile(path, ex);
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading settings: {ex.Message}");
            return;
        }

        if (loaded == null)
        {
            BackUpCorruptFile(path, null);
            return;
        }

        _settings = loaded;
        _engineRegistry.ReloadEngines(_settings.CustomBbpPath, _settings.CustomJavaPath, _settings.CustomJavafoJarPath);
    }

    private static void BackUpCorruptFile(string path, Exception? ex)
    {
        try
        {
            string backupPath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backupPath, overwrite: true);
            Console.WriteLine($"Settings file is corrupt ({ex?.Message ?? "empty"}); backed up to {backupPath}, using defaults.");
        }
        catch (Exception backupEx)
        {
            Console.WriteLine($"Settings file is corrupt and could not be backed up: {backupEx.Message}");
        }
    }

    public Task LoadSettingsAsync()
    {
        LoadSettingsSync();
        OnSettingsChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists settings atomically. Throws if the file cannot be written; in that case the
    /// in-memory settings are left unchanged and <see cref="OnSettingsChanged"/> is not raised.
    /// </summary>
    public Task SaveSettingsAsync(AppSettings newSettings)
    {
        var settings = newSettings.Clone();
        string path = GetConfigFilePath();
        var dict = new Dictionary<string, object>();

        // Read existing dictionary to preserve Language or other keys
        if (File.Exists(path))
        {
            try
            {
                string existingJson = File.ReadAllText(path);
                var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson);
                if (existing != null) dict = existing;
            }
            catch { }
        }

        // Serialize updated settings into dictionary
        string settingsJson = JsonSerializer.Serialize(settings);
        var updatedProps = JsonSerializer.Deserialize<Dictionary<string, object>>(settingsJson);
        if (updatedProps != null)
        {
            foreach (var (k, v) in updatedProps)
            {
                dict[k] = v;
            }
        }

        string finalJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });

        // Write to a temp file in the same directory, then atomically replace the real file
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, finalJson);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }

        _settings = settings;

        // Notify pairing engine registry
        _engineRegistry.ReloadEngines(_settings.CustomBbpPath, _settings.CustomJavaPath, _settings.CustomJavafoJarPath);

        OnSettingsChanged?.Invoke();
        return Task.CompletedTask;
    }
}
