using System.ComponentModel;
using System.Diagnostics;
using CaissaDesk.Components.Common;
using CaissaDesk.Core;
using CaissaDesk.Core.Notifications;
using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Data;
using CaissaDesk.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaissaDesk.Tests;

public class NotificationServiceTests
{
    [Fact]
    public void Show_AddsAndRaisesChanged_DismissRemoves()
    {
        using var svc = new NotificationService();
        int changes = 0;
        svc.Changed += () => changes++;

        var n = svc.Show(NotificationKind.Error, "boom", TimeSpan.Zero);

        Assert.Equal(n, svc.Notifications.Single());
        svc.Dismiss(n.Id);
        svc.Dismiss(n.Id); // second dismiss is a no-op
        Assert.Empty(svc.Notifications);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Show_KeepsAtMostMaxVisible_DroppingOldest()
    {
        using var svc = new NotificationService();
        for (int i = 0; i < NotificationService.MaxVisible + 2; i++)
        {
            svc.Show(NotificationKind.Info, $"m{i}", TimeSpan.Zero);
        }

        Assert.Equal(NotificationService.MaxVisible, svc.Notifications.Count);
        Assert.Equal("m2", svc.Notifications[0].Message);
    }

    [Fact]
    public async Task Show_WithDuration_AutoDismisses()
    {
        using var svc = new NotificationService();
        svc.Show(NotificationKind.Success, "saved", TimeSpan.FromMilliseconds(30));

        for (int i = 0; i < 100 && svc.Notifications.Count > 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.Empty(svc.Notifications);
    }

    [Fact]
    public void Extensions_UseMatchingKind()
    {
        using var svc = new NotificationService();
        Assert.Equal(NotificationKind.Error, svc.ShowError("e").Kind);
        Assert.Equal(NotificationKind.Success, svc.ShowSuccess("s").Kind);
    }
}

public class ExternalLinkServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/home/user/file.txt")]
    public void TryOpen_RejectsNonWebLinks(string? url)
    {
        int starts = 0;
        var svc = new ExternalLinkService(null, _ => { starts++; return null; }, () => true);

        Assert.False(svc.TryOpen(url));
        Assert.Equal(0, starts);
    }

    [Fact]
    public void TryOpen_UsesShellExecute()
    {
        var started = new List<ProcessStartInfo>();
        var svc = new ExternalLinkService(null, psi => { started.Add(psi); return null; }, () => false);

        Assert.True(svc.TryOpen("https://results.example.org/t/42"));

        var psi = Assert.Single(started);
        Assert.True(psi.UseShellExecute);
        Assert.Equal("https://results.example.org/t/42", psi.FileName);
    }

    [Fact]
    public void TryOpen_OnLinux_FallsBackToXdgOpen()
    {
        var started = new List<ProcessStartInfo>();
        var svc = new ExternalLinkService(null, psi =>
        {
            started.Add(psi);
            if (psi.UseShellExecute) throw new Win32Exception("no handler");
            return null;
        }, () => true);

        Assert.True(svc.TryOpen("http://localhost:5200"));

        Assert.Equal(2, started.Count);
        Assert.Equal("xdg-open", started[1].FileName);
        Assert.Equal(["http://localhost:5200/"], started[1].ArgumentList);
    }

    [Fact]
    public void TryOpen_AllLaunchersFail_ReturnsFalse()
    {
        var svc = new ExternalLinkService(null, _ => throw new Win32Exception("nope"), () => true);
        Assert.False(svc.TryOpen("https://example.org"));

        var notLinux = new ExternalLinkService(null, _ => throw new Win32Exception("nope"), () => false);
        Assert.False(notLinux.TryOpen("https://example.org"));
    }
}

public class ConfirmDialogStateTests
{
    [Fact]
    public async Task Request_ThenConfirm_RunsActionOnceAndCloses()
    {
        var state = new ConfirmDialogState();
        int runs = 0;

        state.Request("Delete?", "Really?", () => { runs++; return Task.CompletedTask; }, details: "x", confirmText: "Delete", icon: "trash");

        Assert.True(state.IsOpen);
        Assert.Equal("trash", state.HeaderIcon);
        Assert.Equal("trash", state.ConfirmIcon);
        Assert.True(state.IsDestructive);

        await state.ConfirmAsync();
        await state.ConfirmAsync();

        Assert.False(state.IsOpen);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Cancel_DropsAction()
    {
        var state = new ConfirmDialogState();
        int runs = 0;
        state.Request("t", "m", () => { runs++; return Task.CompletedTask; }, isDestructive: false);

        Assert.False(state.IsDestructive);
        state.Cancel();
        await state.ConfirmAsync();

        Assert.False(state.IsOpen);
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task Confirm_ActionFailure_PropagatesAfterClosing()
    {
        var state = new ConfirmDialogState();
        state.Request("t", "m", () => throw new IOException("disk full"));

        await Assert.ThrowsAsync<IOException>(state.ConfirmAsync);
        Assert.False(state.IsOpen);
    }
}

public class EngineInstallerExtensionsTests
{
    private sealed class FakeInstaller(Func<EngineInstallResult> install) : IEngineInstallerService
    {
        public Task<EngineInstallResult> InstallEngineAsync(string engineId, IProgress<EngineInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(install());
    }

    [Fact]
    public async Task TryInstall_PassesResultThrough()
    {
        var ok = await new FakeInstaller(() => EngineInstallResult.Ok("/x")).TryInstallEngineAsync("bbp", NullLogger.Instance);
        var failed = await new FakeInstaller(() => EngineInstallResult.Fail("hash mismatch")).TryInstallEngineAsync("bbp", NullLogger.Instance);

        Assert.True(ok.Success);
        Assert.False(failed.Success);
        Assert.Equal("hash mismatch", failed.ErrorMessage);
    }

    [Fact]
    public async Task TryInstall_Exception_BecomesFailedResult()
    {
        var result = await new FakeInstaller(() => throw new HttpRequestException("offline")).TryInstallEngineAsync("javafo", NullLogger.Instance);

        Assert.False(result.Success);
        Assert.Equal("offline", result.ErrorMessage);
    }
}

public class TournamentFileExportsTests
{
    private sealed class CapturingExport : IFileExportService
    {
        public (string Title, string FileName, string Content, string[] Extensions)? Last { get; private set; }

        public Task<string?> SaveTextFileAsync(string dialogTitle, string defaultFileName, string content, params (string Description, string[] Extensions)[] filters)
        {
            Last = (dialogTitle, defaultFileName, content, filters.SelectMany(f => f.Extensions).ToArray());
            return Task.FromResult<string?>("/tmp/" + defaultFileName);
        }
    }

    [Theory]
    [InlineData("Open 2026", "Open_2026")]
    [InlineData("A/B: C", "A_B:_C")]
    public void SafeFileName_ReplacesSpacesAndInvalidCharacters(string name, string expectedOnLinux)
    {
        string safe = TournamentFileExports.SafeFileName(name);

        Assert.DoesNotContain(' ', safe);
        Assert.True(safe.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        if (OperatingSystem.IsLinux()) Assert.Equal(expectedOnLinux, safe);
    }

    [Fact]
    public void FileNames_HaveExpectedShape()
    {
        var t = new Tournament { Name = "Club Open" };

        Assert.Equal("Club_Open_20260301.trf", TournamentFileExports.TrfFileName(t, new DateTime(2026, 3, 1)));
        Assert.Equal("Club_Open.cdesk", TournamentFileExports.CdeskFileName("Club Open"));
    }

    [Fact]
    public async Task SaveTrfAndCdesk_UseLocalizedDialogsAndContent()
    {
        var export = new CapturingExport();
        var loc = new LocalizationService(loadPersistedSettings: false);
        var t = new Tournament { Name = "Club Open", Players = [new Player { FullName = "Doe, John", StartRank = 1 }] };

        string? trfPath = await export.SaveTrfAsync(t, loc);
        Assert.EndsWith(".trf", trfPath);
        Assert.Equal(loc["Export_TrfDialogTitle"], export.Last!.Value.Title);
        Assert.Equal(TrfHandler.GenerateTrf(t), export.Last.Value.Content);
        Assert.Equal([".trf"], export.Last.Value.Extensions);

        await export.SaveCdeskAsync("Club Open", "{}", loc);
        Assert.Equal("Club_Open.cdesk", export.Last.Value.FileName);
        Assert.Equal("{}", export.Last.Value.Content);
        Assert.Equal([".cdesk"], export.Last.Value.Extensions);
    }
}
