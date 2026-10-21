using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;
using Xunit;

namespace CaissaDesk.Tests;

public class BbpPairingEngineTests
{
    [Fact]
    public void TestEngine_BbpPairings_SucceedsIfInstalled()
    {
        var registry = new PairingEngineRegistry();
        var bbp = registry.AllEngines.FirstOrDefault(e => e.Id == "bbp");

        if (bbp == null || !bbp.IsAvailable)
        {
            // If binary not found in current environment, skip
            return;
        }

        var result = registry.TestEngine("bbp");
        Assert.True(result.Success, $"bbpPairings TestEngine failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.Pairings);
        Assert.Equal(2, result.Pairings.Count);
    }
}
