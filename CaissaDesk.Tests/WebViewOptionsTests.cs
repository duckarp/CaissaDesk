using CaissaDesk.Core;

namespace CaissaDesk.Tests;

public class WebViewOptionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "caissadesk-webview-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "desk_settings.json");

    public WebViewOptionsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Arguments_AlwaysDisableOcclusionDetection()
    {
        // Without these WebView2 can treat the new window as hidden and never paint it (black window)
        foreach (bool hw in new[] { true, false })
        {
            string args = WebViewOptions.BuildWindowsBrowserArguments(hw);
            Assert.Contains("--disable-features=CalculateNativeWinOcclusion", args);
            Assert.Contains("--disable-backgrounding-occluded-windows", args);
        }
    }

    [Fact]
    public void Arguments_DisableGpuOnlyWhenAccelerationIsOff()
    {
        Assert.DoesNotContain("--disable-gpu", WebViewOptions.BuildWindowsBrowserArguments(true));
        Assert.Contains("--disable-gpu", WebViewOptions.BuildWindowsBrowserArguments(false));
    }

    [Fact]
    public void Acceleration_DefaultsOn_WhenSettingsMissingOrCorrupt()
    {
        Assert.True(WebViewOptions.IsHardwareAccelerationEnabled([], SettingsPath));

        File.WriteAllText(SettingsPath, "{ not json");
        Assert.True(WebViewOptions.IsHardwareAccelerationEnabled([], SettingsPath));
    }

    [Fact]
    public void Acceleration_ReadsSavedSetting()
    {
        File.WriteAllText(SettingsPath, """{ "Language": "sk", "HardwareAccelerationEnabled": false }""");
        Assert.False(WebViewOptions.IsHardwareAccelerationEnabled([], SettingsPath));

        File.WriteAllText(SettingsPath, """{ "HardwareAccelerationEnabled": true }""");
        Assert.True(WebViewOptions.IsHardwareAccelerationEnabled([], SettingsPath));
    }

    [Fact]
    public void Acceleration_CommandLineSwitchWinsOverSettings()
    {
        File.WriteAllText(SettingsPath, """{ "HardwareAccelerationEnabled": true }""");
        Assert.False(WebViewOptions.IsHardwareAccelerationEnabled(["--disable-gpu"], SettingsPath));
    }
}
