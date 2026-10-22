using System.Text.Json;

namespace CaissaDesk.Core;

/// <summary>
/// Chromium switches for the WebView2 control on Windows. Without them some machines show only a black
/// window: WebView2's native occlusion check decides the freshly created Photino window is hidden and never
/// paints it, and some GPU drivers cannot composite the view at all (same fix as Caissalytics).
/// </summary>
public static class WebViewOptions
{
    /// <summary>Command-line switch that turns GPU acceleration off for one launch, without opening Settings.</summary>
    public const string DisableGpuSwitch = "--disable-gpu";

    public static string BuildWindowsBrowserArguments(bool hardwareAcceleration)
    {
        var args = new List<string>
        {
            "--disable-backgrounding-occluded-windows",
            "--enable-features=DirectCompositionUseSystemFrameLatency",
            "--disable-features=CalculateNativeWinOcclusion"
        };

        if (!hardwareAcceleration)
        {
            args.Add("--disable-gpu");
            args.Add("--disable-software-rasterizer");
        }

        return string.Join(" ", args);
    }

    /// <summary>
    /// Read before the app's services exist, so straight from desk_settings.json. Defaults to on when the
    /// file is missing or unreadable; <see cref="DisableGpuSwitch"/> on the command line always turns it off.
    /// </summary>
    public static bool IsHardwareAccelerationEnabled(string[] commandLineArgs, string settingsFilePath)
    {
        if (commandLineArgs.Any(a => string.Equals(a, DisableGpuSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            if (!File.Exists(settingsFilePath)) return true;
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsFilePath));
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "HardwareAccelerationEnabled", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }
        }
        catch (Exception)
        {
            // Corrupt settings are handled (and backed up) by AppSettingsService; render with defaults here
        }

        return true;
    }
}
