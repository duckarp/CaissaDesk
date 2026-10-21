using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public static class TrfHandler
{
    public static string GenerateTrf(Tournament tournament, int? pairingRound = null) =>
        Caissa.Shared.PairingEngine.TrfHandler.GenerateTrf(tournament, pairingRound);
}
