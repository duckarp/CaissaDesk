using System.Globalization;
using System.Text;

namespace CaissaDesk.Core.Standings;

/// <summary>
/// Caches <see cref="ITiebreakService.CalculateStandings"/> output for one tournament and recomputes it only
/// when something the calculation reads has changed (players, ratings, seeding, rounds, results, tie-break
/// order, system or round count). Rendering the standings repeatedly therefore costs one cheap key build
/// instead of a full tie-break computation per render.
/// </summary>
public sealed class StandingsCache
{
    private readonly ITiebreakService _tiebreakService;
    private Tournament? _source;
    private string? _key;
    private List<PlayerStanding> _standings = [];
    private Dictionary<string, PlayerStanding> _byPlayer = [];

    public StandingsCache(ITiebreakService tiebreakService)
    {
        _tiebreakService = tiebreakService;
    }

    /// <summary>How many times the standings were actually recomputed (for diagnostics and tests).</summary>
    public int ComputeCount { get; private set; }

    /// <summary>The ranked standings, in rank order, using the tournament's configured tie-break order.</summary>
    public IReadOnlyList<PlayerStanding> GetStandings(Tournament tournament)
    {
        Refresh(tournament);
        return _standings;
    }

    /// <summary>Total points of a player (0 for unknown players).</summary>
    public double GetPoints(Tournament tournament, string playerId)
    {
        Refresh(tournament);
        return _byPlayer.TryGetValue(playerId, out var standing) ? standing.Points : 0.0;
    }

    /// <summary>Forces the next read to recompute.</summary>
    public void Invalidate()
    {
        _source = null;
        _key = null;
    }

    private void Refresh(Tournament tournament)
    {
        ArgumentNullException.ThrowIfNull(tournament);

        string key = BuildKey(tournament);
        if (ReferenceEquals(_source, tournament) && _key == key) return;

        _standings = _tiebreakService.CalculateStandings(tournament);
        _byPlayer = [];
        foreach (var s in _standings)
        {
            _byPlayer.TryAdd(s.PlayerId, s);
        }

        _source = tournament;
        _key = key;
        ComputeCount++;
    }

    /// <summary>An exact description of every input the tie-break calculation reads.</summary>
    public static string BuildKey(Tournament t)
    {
        var sb = new StringBuilder(256);
        sb.Append((int)t.System).Append('|')
          .Append(t.TotalRounds.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append((int)t.SeedingPrecedence).Append('|');

        if (t.TiebreakOrder != null)
        {
            foreach (var tb in t.TiebreakOrder) sb.Append((int)tb).Append(',');
        }
        sb.Append("|P");

        foreach (var p in t.Players)
        {
            sb.Append(p.Id).Append(':')
              .Append(p.StartRank.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(p.FideRating.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(p.NationalRating.ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        sb.Append("|R");

        foreach (var r in t.Rounds)
        {
            sb.Append(r.RoundNumber.ToString(CultureInfo.InvariantCulture)).Append('[');
            foreach (var pairing in r.Pairings)
            {
                sb.Append(pairing.WhitePlayerId).Append('-')
                  .Append(pairing.BlackPlayerId).Append('=')
                  .Append((int)pairing.Result).Append(';');
            }
            sb.Append(']');
        }

        return sb.ToString();
    }
}
