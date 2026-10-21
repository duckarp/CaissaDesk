using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;

namespace CaissaDesk.Tests;

public class AsyncPairingTests
{
    private static Tournament Players(int count) => new()
    {
        System = TournamentSystem.SwissDutch,
        TotalRounds = 5,
        Players = Enumerable.Range(1, count).Select(r => new Player { Id = $"p{r}", StartRank = r, FullName = $"P{r}" }).ToList()
    };

    /// <summary>Four players with a completed round 1 (p1-p3 1-0, p4-p2 0-1).</summary>
    private static Tournament FourPlayersAfterRound1()
    {
        var t = Players(4);
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { Id = "g1", RoundNumber = 1, BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p3", Result = MatchResult.WhiteWin },
                new Pairing { Id = "g2", RoundNumber = 1, BoardNumber = 2, WhitePlayerId = "p4", BlackPlayerId = "p2", Result = MatchResult.BlackWin }
            ]
        });
        return t;
    }

    // ---------------------------------------------------------------- native engine

    [Fact]
    public async Task Native_Async_MatchesSyncResult()
    {
        var engine = new NativeDutchPairingEngine();
        var t = FourPlayersAfterRound1();

        var sync = engine.GenerateRoundPairings(t, 2);
        var async = await engine.GenerateRoundPairingsAsync(t, 2);

        Assert.True(async.Success, async.ErrorMessage);
        Assert.Equal(
            sync.Pairings.Select(p => (p.WhitePlayerId, p.BlackPlayerId)),
            async.Pairings.Select(p => (p.WhitePlayerId, p.BlackPlayerId)));
    }

    [Fact]
    public async Task Native_SearchBudgetExceeded_ReturnsClearFailure()
    {
        var engine = new NativeDutchPairingEngine { MaxSearchNodes = 1 };

        var result = await engine.GenerateRoundPairingsAsync(FourPlayersAfterRound1(), 2);

        Assert.False(result.Success);
        Assert.Contains("step limit", result.ErrorMessage);
    }

    [Fact]
    public async Task Native_CancelledToken_Throws()
    {
        var engine = new NativeDutchPairingEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.GenerateRoundPairingsAsync(FourPlayersAfterRound1(), 2, cts.Token));
    }

    [Fact]
    public async Task Berger_Async_ReturnsPairings()
    {
        var t = Players(4);
        t.System = TournamentSystem.RoundRobin;

        var result = await new BergerPairingEngine().GenerateRoundPairingsAsync(t, 1);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.Pairings.Count);
    }

    [Fact]
    public async Task Fallback_Async_AttachesWarning()
    {
        var registry = new PairingEngineRegistry();

        var result = await registry.GetEngine("no_such_engine").GenerateRoundPairingsAsync(Players(2), 1);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Warning);
    }

    // ---------------------------------------------------------------- live model isolation

    [Fact]
    public void Snapshot_CopiesTournamentWithOwnRoundsAndPlayersLists()
    {
        var live = FourPlayersAfterRound1();
        live.Name = "Open";
        live.PairingEngineId = "bbp";
        var liveRounds = live.Rounds;
        var livePlayers = live.Players;

        var snapshot = PairingSnapshot.WithRounds(live, []);
        snapshot.Rounds.Add(new Round { RoundNumber = 9 });
        snapshot.Players.RemoveAt(0);

        Assert.NotSame(live, snapshot);
        Assert.Equal("Open", snapshot.Name);
        Assert.Equal("bbp", snapshot.PairingEngineId);
        Assert.Equal(live.Id, snapshot.Id);
        Assert.Same(liveRounds, live.Rounds);
        Assert.Single(live.Rounds);
        Assert.Equal(1, live.Rounds[0].RoundNumber);
        Assert.Same(livePlayers, live.Players);
        Assert.Equal(4, live.Players.Count);
    }

    [Fact]
    public async Task PairingAgainstHypotheticalHistory_NeverMutatesLiveTournament()
    {
        var live = FourPlayersAfterRound1();
        var liveRounds = live.Rounds;
        var engine = new BlockingEngine();

        // Re-pair round 1 against an empty history, as "Reset & regenerate" does
        var pairing = engine.GenerateRoundPairingsAsync(PairingSnapshot.WithRounds(live, []), 1);

        // While the engine is still running the live model still exposes its real rounds
        var seen = await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(seen.Rounds);
        Assert.Same(liveRounds, live.Rounds);
        Assert.Single(live.Rounds);
        Assert.Equal(2, live.Rounds[0].Pairings.Count);

        engine.Release.SetResult();
        var result = await pairing;

        Assert.True(result.Success);
        Assert.Same(liveRounds, live.Rounds);
        Assert.Single(live.Rounds);
    }

    [Fact]
    public async Task NativeAsync_OnSnapshot_LeavesLiveRoundsUntouched()
    {
        var live = FourPlayersAfterRound1();
        var liveRounds = live.Rounds;

        var result = await new NativeDutchPairingEngine().GenerateRoundPairingsAsync(PairingSnapshot.WithRounds(live, []), 1);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Same(liveRounds, live.Rounds);
        Assert.Single(live.Rounds);
    }

    private sealed class BlockingEngine : IPairingEngine
    {
        public TaskCompletionSource<Tournament> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "blocking";
        public string DisplayName => "Blocking";
        public bool IsAvailable => true;

        public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber) => throw new NotSupportedException();

        public async Task<PairingResult> GenerateRoundPairingsAsync(Tournament tournament, int roundNumber, CancellationToken cancellationToken = default)
        {
            Started.SetResult(tournament);
            await Release.Task.WaitAsync(cancellationToken);
            return PairingResult.Ok([]);
        }
    }

    // ---------------------------------------------------------------- external engines

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static string WriteScript(string body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"caissadesk_fake_engine_{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public async Task ExternalRunAsync_ParsesOutputOfFakeEngine()
    {
        if (OperatingSystem.IsWindows()) return;

        // Arguments: <trf> -p <output>
        string script = WriteScript("printf '3\\n1 3\\n4 2\\n5 0\\n' > \"$3\"");
        try
        {
            var result = await ExternalPairingEngineRunner.RunAsync("fake", Players(5), 1, script, (trf, output) => [trf, "-p", output]);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(3, result.Pairings.Count);
            Assert.Equal(MatchResult.ByeFull, result.Pairings[2].Result);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task ExternalRunAsync_TimeoutKillsEngineAndFails()
    {
        if (OperatingSystem.IsWindows()) return;

        string script = WriteScript("exec sleep 30");
        try
        {
            var sw = Stopwatch.StartNew();
            var result = await ExternalPairingEngineRunner.RunAsync("fake", Players(4), 1, script,
                (trf, output) => [trf, "-p", output], TimeSpan.FromMilliseconds(300));
            sw.Stop();

            Assert.False(result.Success);
            Assert.Contains("timed out", result.ErrorMessage);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task RunProcessAsync_Cancellation_KillsProcessAndThrows()
    {
        if (OperatingSystem.IsWindows()) return;

        var psi = new ProcessStartInfo("sleep")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("30");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ExternalPairingEngineRunner.RunProcessAsync(psi, TimeSpan.FromSeconds(60), cts.Token));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Registry_TestWithUnsavedPaths_DoesNotTouchLiveEngines()
    {
        var registry = new PairingEngineRegistry();
        var liveBbp = registry.AllEngines.OfType<BbpPairingEngine>().Single();
        string? livePath = liveBbp.ExecutablePath;

        string fake = Path.Combine(Path.GetTempPath(), $"caissadesk_not_an_engine_{Guid.NewGuid():N}");
        File.WriteAllText(fake, "not executable");
        try
        {
            var result = await registry.TestEngineWithPathsAsync("bbp", fake, null, null);

            Assert.False(result.Success);
            Assert.Equal(livePath, liveBbp.ExecutablePath);
        }
        finally
        {
            File.Delete(fake);
        }
    }

    [Fact]
    public async Task Registry_TestEngineAsync_NativeSucceeds()
    {
        var result = await new PairingEngineRegistry().TestEngineAsync("native_dutch");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.Pairings.Count);
    }

    // ---------------------------------------------------------------- installer integrity

    private sealed class StaticContentHandler(byte[] content) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }

    [Fact]
    public async Task Download_HashMismatch_IsRejectedAndFileRemoved()
    {
        var handler = new StaticContentHandler("tampered binary"u8.ToArray());
        using var http = new HttpClient(handler);
        string dest = Path.Combine(Path.GetTempPath(), $"caissadesk_dl_{Guid.NewGuid():N}.tmp");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => EngineInstallerService.DownloadVerifiedAsync(http, EngineInstallerService.JaVaFo, dest));

        Assert.Contains("Integrity check failed", ex.Message);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public async Task Download_MatchingHash_IsKept()
    {
        byte[] content = "genuine binary"u8.ToArray();
        var asset = new EngineAsset("1.0", "https://example.invalid/engine.bin", Convert.ToHexStringLower(SHA256.HashData(content)));
        using var http = new HttpClient(new StaticContentHandler(content));
        string dest = Path.Combine(Path.GetTempPath(), $"caissadesk_dl_{Guid.NewGuid():N}.tmp");

        try
        {
            await EngineInstallerService.DownloadVerifiedAsync(http, asset, dest);
            Assert.Equal(content, await File.ReadAllBytesAsync(dest));
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task Download_WithoutPinnedHash_FailsClosedWithoutRequest()
    {
        var handler = new StaticContentHandler([1, 2, 3]);
        using var http = new HttpClient(handler);
        var asset = new EngineAsset("1.0", "https://example.invalid/engine.bin", "TODO");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => EngineInstallerService.DownloadVerifiedAsync(http, asset, Path.Combine(Path.GetTempPath(), $"caissadesk_dl_{Guid.NewGuid():N}.tmp")));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public void PinnedAssets_UseVersionedUrlsAndValidHashes()
    {
        foreach (var asset in new[] { EngineInstallerService.BbpWindowsX64, EngineInstallerService.BbpLinuxX64, EngineInstallerService.JaVaFo })
        {
            Assert.StartsWith("https://", asset.Url);
            Assert.DoesNotContain("/current/", asset.Url);
            Assert.Contains(asset.Version, asset.Url);
            Assert.Matches("^[0-9a-f]{64}$", asset.Sha256);
        }
    }

    [Fact]
    public async Task TarExtraction_SkipsLinksAndTakesRegularFile()
    {
        string archive = Path.Combine(Path.GetTempPath(), $"caissadesk_tar_{Guid.NewGuid():N}.tar.gz");
        string dest = Path.Combine(Path.GetTempPath(), $"caissadesk_bin_{Guid.NewGuid():N}");
        byte[] payload = "real engine"u8.ToArray();

        try
        {
            await using (var fs = File.Create(archive))
            await using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            await using (var writer = new TarWriter(gz))
            {
                await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "pkg/bbpPairings") { LinkName = "/etc/passwd" });
                await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.Directory, "pkg/bbpPairings.exe/"));
                await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "pkg/bbpPairings.exe") { DataStream = new MemoryStream(payload) });
            }

            bool found = await EngineInstallerService.ExtractFromTarGzAsync(archive, dest);

            Assert.True(found);
            Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
        }
        finally
        {
            File.Delete(archive);
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task TarExtraction_OnlyLinks_FindsNothing()
    {
        string archive = Path.Combine(Path.GetTempPath(), $"caissadesk_tar_{Guid.NewGuid():N}.tar.gz");
        string dest = Path.Combine(Path.GetTempPath(), $"caissadesk_bin_{Guid.NewGuid():N}");

        try
        {
            await using (var fs = File.Create(archive))
            await using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            await using (var writer = new TarWriter(gz))
            {
                await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.HardLink, "pkg/bbpPairings") { LinkName = "/bin/sh" });
            }

            Assert.False(await EngineInstallerService.ExtractFromTarGzAsync(archive, dest));
            Assert.False(File.Exists(dest));
        }
        finally
        {
            File.Delete(archive);
        }
    }
}
