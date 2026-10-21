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
        try
        {
            string path = GetConfigFilePath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (loaded != null)
                {
                    _settings = loaded;
                    _engineRegistry.ReloadEngines(_settings.CustomBbpPath, _settings.CustomJavaPath, _settings.CustomJavafoJarPath);
                }
            }
        }
        catch
        {
            // Use defaults if config cannot be read
        }
    }

    public Task LoadSettingsAsync()
    {
        LoadSettingsSync();
        OnSettingsChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task SaveSettingsAsync(AppSettings newSettings)
    {
        _settings = newSettings.Clone();

        try
        {
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
            string settingsJson = JsonSerializer.Serialize(_settings);
            var updatedProps = JsonSerializer.Deserialize<Dictionary<string, object>>(settingsJson);
            if (updatedProps != null)
            {
                foreach (var (k, v) in updatedProps)
                {
                    dict[k] = v;
                }
            }

            string finalJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, finalJson);

            // Notify pairing engine registry
            _engineRegistry.ReloadEngines(_settings.CustomBbpPath, _settings.CustomJavaPath, _settings.CustomJavafoJarPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }

        OnSettingsChanged?.Invoke();
        return Task.CompletedTask;
    }
}
