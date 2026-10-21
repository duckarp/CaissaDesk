using System.Text.RegularExpressions;
using CaissaDesk.Core.Roster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaissaDesk.Data;

public enum PlayerLookupStatus
{
    Found,
    NotFound,
    Failed
}

public sealed class PlayerLookupOutcome
{
    public PlayerLookupStatus Status { get; init; }

    /// <summary>Name of the matched FIDE (or, failing that, national) record.</summary>
    public string? MatchedName { get; init; }

    /// <summary>Exception message when <see cref="Status"/> is <see cref="PlayerLookupStatus.Failed"/>.</summary>
    public string? FailureMessage { get; init; }
}

/// <summary>
/// FIDE + national database lookups for the roster: the "smart" FIDE search (by ID, falling back to a scored
/// name match), filling the player form from the found records, and bulk-syncing a whole roster.
/// </summary>
public interface IPlayerLookupService
{
    /// <summary>Looks a player up by FIDE ID, falling back to the best name (+ federation) match.</summary>
    Task<FidePlayerCard?> SmartFideLookupAsync(string? fideId, string? lastName, string? firstName, string? federation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fills <paramref name="form"/> from FIDE and the national database. User-typed names are kept when they
    /// only differ from the FIDE spelling by diacritics.
    /// </summary>
    Task<PlayerLookupOutcome> LookupIntoFormAsync(PlayerFormModel form, TimeControlCategory? timeControl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes every player from FIDE and the national database. Failures for one player are logged and
    /// skipped. <paramref name="onProgress"/> receives (current, total) before each player.
    /// Returns how many players were matched in FIDE.
    /// </summary>
    Task<int> SyncPlayersAsync(IReadOnlyList<Player> players, TimeControlCategory? timeControl, Action<int, int>? onProgress = null, CancellationToken cancellationToken = default);
}

public partial class PlayerLookupService : IPlayerLookupService
{
    private readonly IFideScoutingService _fide;
    private readonly INationalScoutingService _national;
    private readonly ILogger<PlayerLookupService> _logger;

    public PlayerLookupService(IFideScoutingService fide, INationalScoutingService national, ILogger<PlayerLookupService>? logger = null)
    {
        _fide = fide;
        _national = national;
        _logger = logger ?? NullLogger<PlayerLookupService>.Instance;
    }

    /// <summary>Pause between players during a bulk sync, to stay polite to the rating servers.</summary>
    public TimeSpan SyncThrottle { get; set; } = TimeSpan.FromMilliseconds(80);

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex NonDigits();

    /// <summary>The rating matching the tournament's time control, falling back to the other lists.</summary>
    public static int GetRatingForTimeControl(FidePlayerCard card, TimeControlCategory? timeControl) => timeControl switch
    {
        null => card.StandardElo ?? card.RapidElo ?? card.BlitzElo ?? 0,
        TimeControlCategory.Rapid => card.RapidElo ?? card.StandardElo ?? card.BlitzElo ?? 0,
        TimeControlCategory.Blitz => card.BlitzElo ?? card.RapidElo ?? card.StandardElo ?? 0,
        _ => card.StandardElo ?? card.RapidElo ?? card.BlitzElo ?? 0
    };

    /// <summary>"F" for a female card gender or a women's title (W...), otherwise "M".</summary>
    public static string NormalizeGender(string? cardGender, string? title)
    {
        if (!string.IsNullOrWhiteSpace(cardGender) && cardGender.Trim().StartsWith("f", StringComparison.OrdinalIgnoreCase))
            return "F";
        if (!string.IsNullOrWhiteSpace(title) && title.Trim().StartsWith("W", StringComparison.OrdinalIgnoreCase))
            return "F";
        return "M";
    }

    public async Task<FidePlayerCard?> SmartFideLookupAsync(string? fideId, string? lastName, string? firstName, string? federation, CancellationToken cancellationToken = default)
    {
        // 1. Try by FIDE ID if provided
        if (!string.IsNullOrWhiteSpace(fideId))
        {
            string cleanId = NonDigits().Replace(fideId.Trim(), "");
            if (!string.IsNullOrEmpty(cleanId))
            {
                var card = await _fide.GetPlayerCardAsync(cleanId, cancellationToken);
                if (card != null)
                {
                    return card;
                }
            }
        }

        // 2. Fall back to search by Name (+ Federation)
        string cleanLast = (lastName ?? "").Trim();
        string cleanFirst = (firstName ?? "").Trim();
        string query = string.Join(" ", new[] { cleanLast, cleanFirst }.Where(s => !string.IsNullOrEmpty(s)));

        if (query.Length < 2) return null;

        var results = await _fide.SearchPlayersByNameAsync(query, cancellationToken: cancellationToken);
        if ((results == null || results.Count == 0) && cleanLast.Length >= 2 && !string.IsNullOrWhiteSpace(cleanFirst))
        {
            // If full name query produced no results, try searching just the last name
            results = await _fide.SearchPlayersByNameAsync(cleanLast, cancellationToken: cancellationToken);
        }

        if (results == null || results.Count == 0) return null;

        var match = SelectBestFideMatch(results, cleanLast, cleanFirst, federation);
        if (match == null || string.IsNullOrWhiteSpace(match.FideId)) return null;

        var matchedCard = await _fide.GetPlayerCardAsync(match.FideId, cancellationToken);
        if (matchedCard != null)
        {
            return matchedCard;
        }

        return new FidePlayerCard
        {
            FideId = match.FideId,
            FullName = match.FullName,
            Title = match.Title,
            FederationCode = FideCountryHelper.ToFideCode(match.Federation) ?? match.Federation,
            Federation = match.Federation,
            StandardElo = match.StandardElo,
            RapidElo = match.RapidElo,
            BlitzElo = match.BlitzElo,
            BirthYear = match.BirthYear
        };
    }

    /// <summary>
    /// Scores search results against the wanted name and federation. A candidate whose last name does not
    /// match at all is never picked; returns null when no candidate scores above zero.
    /// </summary>
    public static FideSearchResult? SelectBestFideMatch(
        List<FideSearchResult>? results,
        string? targetLastName,
        string? targetFirstName,
        string? targetFed)
    {
        if (results == null || results.Count == 0) return null;

        string tLast = Player.RemoveDiacritics(targetLastName ?? "").Trim().ToLowerInvariant();
        string tFirst = Player.RemoveDiacritics(targetFirstName ?? "").Trim().ToLowerInvariant();
        string? tFedCode = FideCountryHelper.ToFideCode(targetFed);

        FideSearchResult? bestResult = null;
        int bestScore = -1;

        foreach (var r in results)
        {
            var (cLastRaw, cFirstRaw) = Player.SplitFullName(r.FullName);
            string cLast = Player.RemoveDiacritics(cLastRaw).Trim().ToLowerInvariant();
            string cFirst = Player.RemoveDiacritics(cFirstRaw).Trim().ToLowerInvariant();
            string? cFedCode = FideCountryHelper.ToFideCode(r.Federation);

            int score = 0;

            // Last name scoring - candidate must match last name if provided
            if (!string.IsNullOrEmpty(tLast))
            {
                if (cLast == tLast)
                {
                    score += 100;
                }
                else if (cLast.StartsWith(tLast) || tLast.StartsWith(cLast))
                {
                    score += 60;
                }
                else if (cLast.Contains(tLast) || tLast.Contains(cLast))
                {
                    score += 40;
                }
                else
                {
                    // Fatal mismatch: candidate with different last name must NOT be picked!
                    continue;
                }
            }
            else
            {
                score += 10;
            }

            // First name scoring
            if (!string.IsNullOrEmpty(tFirst))
            {
                if (cFirst == tFirst)
                {
                    score += 60;
                }
                else if (cFirst.StartsWith(tFirst) || tFirst.StartsWith(cFirst))
                {
                    score += 40;
                }
                else if (cFirst.Contains(tFirst) || tFirst.Contains(cFirst))
                {
                    score += 25;
                }
                else
                {
                    score -= 30;
                }
            }

            // Federation scoring
            if (!string.IsNullOrEmpty(tFedCode))
            {
                score += string.Equals(cFedCode, tFedCode, StringComparison.OrdinalIgnoreCase) ? 50 : -20;
            }

            if (r.StandardElo.HasValue && r.StandardElo.Value > 0)
            {
                score += 5;
            }
            if (r.BirthYear.HasValue)
            {
                score += 1;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestResult = r;
            }
        }

        return bestScore > 0 ? bestResult : null;
    }

    public async Task<PlayerLookupOutcome> LookupIntoFormAsync(PlayerFormModel form, TimeControlCategory? timeControl, CancellationToken cancellationToken = default)
    {
        try
        {
            string? matchedName = null;

            // 1. FIDE Lookup
            var card = await SmartFideLookupAsync(form.FideId, form.LastName, form.FirstName, form.Federation, cancellationToken);
            if (card != null)
            {
                form.FideId = card.FideId;
                var (last, first) = Player.SplitFullName(card.FullName);

                // Preserve user-entered diacritics if the base name matches (e.g. "Kamenický" vs "Kamenicky")
                if (!SameBaseName(form.LastName, last)) form.LastName = last;
                if (!SameBaseName(form.FirstName, first)) form.FirstName = first;

                form.Title = card.TitleAbbreviation ?? "";
                string? resolvedFed = FideCountryHelper.ToFideCode(card.FederationCode) ?? FideCountryHelper.ToFideCode(card.Federation);
                form.Federation = resolvedFed ?? card.FederationCode ??
                    (!string.IsNullOrWhiteSpace(card.Federation) && card.Federation.Length == 3 ? card.Federation.ToUpperInvariant() : form.Federation);
                form.FideRating = GetRatingForTimeControl(card, timeControl);
                if (card.BirthYear.HasValue) form.BirthYear = card.BirthYear;
                form.Gender = NormalizeGender(card.Gender, card.TitleAbbreviation);

                matchedName = card.FullName;
            }

            // 2. National Database Lookup (e.g. SŠZ / chess.sk for SVK)
            string targetFed = form.Federation;
            if (string.IsNullOrWhiteSpace(targetFed) && card != null)
            {
                targetFed = card.FederationCode ?? card.Federation ?? "";
            }

            try
            {
                var nationalCard = await _national.LookupPlayerAsync(
                    form.NationalId,
                    card?.FideId ?? form.FideId,
                    form.LastName,
                    form.FirstName,
                    targetFed,
                    cancellationToken);

                if (nationalCard != null)
                {
                    if (!string.IsNullOrWhiteSpace(nationalCard.NationalId)) form.NationalId = nationalCard.NationalId;
                    if (!string.IsNullOrWhiteSpace(nationalCard.Club)) form.Club = nationalCard.Club;
                    if (nationalCard.NationalRating is > 0) form.NationalRating = nationalCard.NationalRating.Value;

                    // If the FIDE card was not found earlier, fill in basic identity from the national database
                    if (card == null)
                    {
                        if (!string.IsNullOrWhiteSpace(nationalCard.FideId))
                        {
                            form.FideId = nationalCard.FideId;
                            var fetchedCard = await _fide.GetPlayerCardAsync(nationalCard.FideId, cancellationToken);
                            if (fetchedCard != null)
                            {
                                form.Title = fetchedCard.TitleAbbreviation ?? "";
                                form.FideRating = GetRatingForTimeControl(fetchedCard, timeControl);
                                if (fetchedCard.BirthYear.HasValue) form.BirthYear = fetchedCard.BirthYear;
                                form.Gender = NormalizeGender(fetchedCard.Gender, fetchedCard.TitleAbbreviation);
                            }
                        }

                        var (nLast, nFirst) = Player.SplitFullName(nationalCard.FullName);
                        if (string.IsNullOrWhiteSpace(form.LastName)) form.LastName = nLast;
                        if (string.IsNullOrWhiteSpace(form.FirstName)) form.FirstName = nFirst;
                        if (string.IsNullOrWhiteSpace(form.Federation) && !string.IsNullOrWhiteSpace(nationalCard.Federation))
                        {
                            form.Federation = nationalCard.Federation;
                        }

                        matchedName = nationalCard.FullName;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "National database lookup failed for {LastName}, {FirstName}.", form.LastName, form.FirstName);
            }

            return string.IsNullOrEmpty(matchedName)
                ? new PlayerLookupOutcome { Status = PlayerLookupStatus.NotFound }
                : new PlayerLookupOutcome { Status = PlayerLookupStatus.Found, MatchedName = matchedName };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Player lookup failed for {LastName}, {FirstName}.", form.LastName, form.FirstName);
            return new PlayerLookupOutcome { Status = PlayerLookupStatus.Failed, FailureMessage = ex.Message };
        }
    }

    private static bool SameBaseName(string? typed, string found) =>
        !string.IsNullOrWhiteSpace(typed) &&
        string.Equals(Player.RemoveDiacritics(typed).Trim(), Player.RemoveDiacritics(found).Trim(), StringComparison.OrdinalIgnoreCase);

    public async Task<int> SyncPlayersAsync(IReadOnlyList<Player> players, TimeControlCategory? timeControl, Action<int, int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        int updatedCount = 0;
        int current = 0;

        // Snapshot the list: the roster may be edited while the sync awaits the network
        var snapshot = players.ToList();
        foreach (var player in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(++current, snapshot.Count);

            try
            {
                var card = await SmartFideLookupAsync(player.FideId, player.LastName, player.FirstName, player.Federation, cancellationToken);
                if (card != null)
                {
                    if (string.IsNullOrWhiteSpace(player.FideId) && !string.IsNullOrWhiteSpace(card.FideId))
                    {
                        player.FideId = card.FideId;
                    }

                    if (!string.IsNullOrWhiteSpace(card.TitleAbbreviation))
                    {
                        player.Title = card.TitleAbbreviation;
                    }

                    string? fedCode = FideCountryHelper.ToFideCode(card.FederationCode) ?? FideCountryHelper.ToFideCode(card.Federation);
                    if (!string.IsNullOrWhiteSpace(fedCode))
                    {
                        player.Federation = fedCode;
                    }

                    int rating = GetRatingForTimeControl(card, timeControl);
                    if (rating > 0)
                    {
                        player.FideRating = rating;
                    }

                    if (card.BirthYear.HasValue)
                    {
                        player.BirthYear = card.BirthYear;
                    }

                    player.Gender = NormalizeGender(card.Gender, card.TitleAbbreviation);
                    updatedCount++;
                }

                // Also sync national details (club, national rating, national ID)
                try
                {
                    var nationalCard = await _national.LookupPlayerAsync(
                        player.NationalId,
                        card?.FideId ?? player.FideId,
                        player.LastName,
                        player.FirstName,
                        player.Federation,
                        cancellationToken);

                    if (nationalCard != null)
                    {
                        if (string.IsNullOrWhiteSpace(player.NationalId) && !string.IsNullOrWhiteSpace(nationalCard.NationalId))
                        {
                            player.NationalId = nationalCard.NationalId;
                        }
                        if (string.IsNullOrWhiteSpace(player.Club) && !string.IsNullOrWhiteSpace(nationalCard.Club))
                        {
                            player.Club = nationalCard.Club;
                        }
                        if (nationalCard.NationalRating is > 0)
                        {
                            player.NationalRating = nationalCard.NationalRating.Value;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "National sync failed for {Player}.", player.FullName);
                }

                if (SyncThrottle > TimeSpan.Zero)
                {
                    await Task.Delay(SyncThrottle, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "FIDE sync failed for {Player}.", player.FullName);
            }
        }

        return updatedCount;
    }
}
