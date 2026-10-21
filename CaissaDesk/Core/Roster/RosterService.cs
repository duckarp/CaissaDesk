namespace CaissaDesk.Core.Roster;

/// <summary>Roster mutations of the tournament workbench (persisting is the caller's job).</summary>
public interface IRosterService
{
    /// <summary>Registers a player from the form at the bottom of the seeding list.</summary>
    Player AddPlayer(Tournament tournament, PlayerFormModel form);

    /// <summary>Removes a player and renumbers the remaining start ranks 1..n in their current order.</summary>
    bool RemovePlayer(Tournament tournament, string playerId);

    /// <summary>
    /// Re-seeds by effective rating (per the tournament's seeding precedence), then title, then name; the
    /// player list is replaced by the sorted list. Returns false when there is nobody to seed.
    /// </summary>
    bool Reseed(Tournament tournament);
}

public class RosterService : IRosterService
{
    public Player AddPlayer(Tournament tournament, PlayerFormModel form)
    {
        var player = form.ToNewPlayer(tournament.Players.Count + 1);
        tournament.Players.Add(player);
        return player;
    }

    public bool RemovePlayer(Tournament tournament, string playerId)
    {
        if (tournament.Players.RemoveAll(p => p.Id == playerId) == 0) return false;

        int rank = 1;
        foreach (var p in tournament.Players.OrderBy(p => p.StartRank))
        {
            p.StartRank = rank++;
        }
        return true;
    }

    public bool Reseed(Tournament tournament)
    {
        if (tournament.Players.Count == 0) return false;

        var sorted = tournament.Players
            .OrderByDescending(p => p.EffectiveRating(tournament.SeedingPrecedence))
            .ThenByDescending(p => Player.GetTitleWeight(p.Title))
            .ThenBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].StartRank = i + 1;
        }

        tournament.Players = sorted;
        return true;
    }
}
