using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public static class TrfHandler
{
    public static string GenerateTrf(Tournament tournament) =>
        Caissa.Shared.PairingEngine.TrfHandler.GenerateTrf(tournament);
}
