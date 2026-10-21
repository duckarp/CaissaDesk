using System.Diagnostics;
using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;

namespace CaissaDesk.Tests;

public class ExternalPairingEngineRunnerTests
{
    private static Tournament FivePlayers()
    {
        var t = new Tournament
        {
            System = TournamentSystem.SwissDutch,
            TotalRounds = 5,
            Players = Enumerable.Range(1, 5).Select(r => new Player { Id = $"p{r}", StartRank = r, FullName = $"P{r}" }).ToList()
        };
        return t;
    }

    [Fact]
    public void Parse_ValidOutput_BuildsGamesPairingByeAndRequestedByes()
    {
        var t = FivePlayers();
        t.Players[4].ByeRequests[1] = ByeType.HalfPoint; // p5 asked for a ½ bye: engine pairs p1..p4

        var result = ExternalPairingEngineRunner.ParsePairingsOutput("engine", t, "2\n1 3\n4 2\n", 1);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(3, result.Pairings.Count);
        Assert.Equal(("p1", "p3"), (result.Pairings[0].WhitePlayerId, result.Pairings[0].BlackPlayerId));
        Assert.Equal(MatchResult.ByeHalf, result.Pairings[2].Result);
        Assert.Equal("p5", result.Pairings[2].WhitePlayerId);
        Assert.Equal([1, 2, 3], result.Pairings.Select(p => p.BoardNumber));
    }

    [Fact]
    public void Parse_PairingAllocatedBye_IsRecordedAsByeFull()
    {
        var t = FivePlayers();

        var result = ExternalPairingEngineRunner.ParsePairingsOutput("engine", t, "3\n1 3\n4 2\n5 0\n", 1);

        Assert.True(result.Success, result.ErrorMessage);
        var bye = result.Pairings.Single(p => p.IsBye);
        Assert.Equal("p5", bye.WhitePlayerId);
        Assert.Equal(MatchResult.ByeFull, bye.Result);
    }

    [Theory]
    [InlineData("3\n1 3\n4 2\n9 0\n", "unknown starting rank 9")]
    [InlineData("3\n1 3\n4 2\n3 5\n", "more than once")]
    [InlineData("2\n1 3\n4 2\n", "did not pair")]
    [InlineData("1\n1 3\n", "did not pair")]
    [InlineData("3\n1 3\n4 2\nfoo bar\n", "unreadable")]
    public void Parse_InvalidOutput_Fails(string output, string expectedError)
    {
        var t = FivePlayers();

        var result = ExternalPairingEngineRunner.ParsePairingsOutput("engine", t, output, 1);

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.ErrorMessage);
    }

    [Fact]
    public void Parse_TwoPairingByes_Fails()
    {
        var t = FivePlayers();
        t.Players.Add(new Player { Id = "p6", StartRank = 6, FullName = "P6" });

        var result = ExternalPairingEngineRunner.ParsePairingsOutput("engine", t, "4\n1 3\n4 2\n5 0\n6 0\n", 1);

        Assert.False(result.Success);
        Assert.Contains("more than one pairing bye", result.ErrorMessage);
    }

    [Fact]
    public void Parse_WithdrawnPlayerInOutput_Fails()
    {
        var t = FivePlayers();
        t.Players[4].Status = PlayerStatus.Withdrawn;

        var result = ExternalPairingEngineRunner.ParsePairingsOutput("engine", t, "3\n1 3\n4 2\n5 0\n", 1);

        Assert.False(result.Success);
        Assert.Contains("not available", result.ErrorMessage);
    }

    [Fact]
    public void Run_DuplicateStartRanks_FailsBeforeStartingTheEngine()
    {
        var t = FivePlayers();
        t.Players[4].StartRank = 1;

        var result = ExternalPairingEngineRunner.Run("engine", t, 1, "/nonexistent/engine", (_, _) => []);

        Assert.False(result.Success);
        Assert.Contains("Starting rank 1", result.ErrorMessage);
    }

    [Fact]
    public void RunProcess_KillsProcessThatExceedsTimeout()
    {
        if (OperatingSystem.IsWindows()) return;

        var psi = new ProcessStartInfo("sleep")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("30");

        var sw = Stopwatch.StartNew();
        var run = ExternalPairingEngineRunner.RunProcess(psi, TimeSpan.FromMilliseconds(300));
        sw.Stop();

        Assert.NotNull(run.Error);
        Assert.Contains("timed out", run.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void RunProcess_DrainsLargeOutputWithoutDeadlock()
    {
        if (OperatingSystem.IsWindows()) return;

        // ~1 MB on stdout and stderr: far beyond the pipe buffer, would hang if not drained concurrently
        var psi = new ProcessStartInfo("sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("head -c 1000000 /dev/zero | tr '\\0' a; head -c 1000000 /dev/zero | tr '\\0' b 1>&2");

        var run = ExternalPairingEngineRunner.RunProcess(psi, TimeSpan.FromSeconds(20));

        Assert.Null(run.Error);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1_000_000, run.StdOut.Length);
        Assert.Equal(1_000_000, run.StdErr.Length);
    }
}
