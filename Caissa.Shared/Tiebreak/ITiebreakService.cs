using Caissa.Shared.Models;

namespace Caissa.Shared.Tiebreak;

public interface ITiebreakService
{
    List<PlayerStanding> CalculateStandings(Tournament tournament, List<TiebreakType>? tiebreakOrder = null);
}
