using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;

namespace CaissaDesk.Tests;

public class PairingEngineRegistryTests
{
    [Fact]
    public void ResolveEngine_UnavailableEngine_ReportsFallbackToNative()
    {
        var registry = new PairingEngineRegistry();

        var resolution = registry.ResolveEngine("no_such_engine");

        Assert.True(resolution.IsFallback);
        Assert.Equal("native_dutch", resolution.Engine.Id);
        Assert.Contains("no_such_engine", resolution.FallbackWarning);
        Assert.Contains("fallback", resolution.Engine.DisplayName);
    }

    [Fact]
    public void GetEngine_Fallback_AttachesWarningToEveryResult()
    {
        var registry = new PairingEngineRegistry();
        var tournament = new Tournament
        {
            Players =
            [
                new Player { Id = "a", StartRank = 1, FullName = "A" },
                new Player { Id = "b", StartRank = 2, FullName = "B" }
            ]
        };

        var result = registry.GetEngine("no_such_engine").GenerateRoundPairings(tournament, 1);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void ResolveEngine_AvailableEngine_IsNotAFallback()
    {
        var registry = new PairingEngineRegistry();

        var resolution = registry.ResolveEngine("native_dutch");

        Assert.False(resolution.IsFallback);
        Assert.IsType<NativeDutchPairingEngine>(resolution.Engine);
    }
}
