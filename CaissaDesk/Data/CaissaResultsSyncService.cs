using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Caissa.Shared.Models;
using Caissa.Shared.Security;
using Caissa.Shared.Sync;
using Microsoft.Extensions.Logging;

namespace CaissaDesk.Data;

public class CaissaResultsSyncService : ICaissaResultsSyncService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppSettingsService _settingsService;
    private readonly ILogger<CaissaResultsSyncService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CaissaResultsSyncService(
        IHttpClientFactory httpClientFactory,
        IAppSettingsService settingsService,
        ILogger<CaissaResultsSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsSyncConfigured => !string.IsNullOrWhiteSpace(_settingsService.Settings.CaissaResultsApiKey);

    private string GetBaseUrl()
    {
        var settings = _settingsService.Settings;
        var url = settings.CaissaResultsUrl?.TrimEnd('/');
        return string.IsNullOrEmpty(url) ? "http://localhost:5200" : url;
    }

    public string GetPortalUrl(string tournamentId)
    {
        return $"{GetBaseUrl()}/tournament/{Uri.EscapeDataString(tournamentId ?? string.Empty)}";
    }

    /// <summary>
    /// Returns an error message when signed (credential-bearing) requests must not be sent to
    /// <paramref name="baseUrl"/>: plain http:// is only allowed for loopback hosts (a local
    /// development server); anywhere else the request would travel unencrypted. Returns null when OK.
    /// </summary>
    public static string? GetInsecureTransportError(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return $"Invalid CaissaResults server URL '{baseUrl}'. Please check Settings.";
        }
        if (uri.Scheme == Uri.UriSchemeHttps) return null;
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            return uri.IsLoopback
                ? null
                : $"Refusing to send credentials over unencrypted HTTP to '{uri.Host}'. Use an https:// CaissaResults URL in Settings (plain http:// is only allowed for localhost).";
        }
        return $"Unsupported URL scheme '{uri.Scheme}' for the CaissaResults server. Use https://.";
    }

    public async Task<bool> PingServerAsync(string? serverUrl = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var baseUrl = serverUrl?.TrimEnd('/') ?? GetBaseUrl();
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            using var response = await client.GetAsync($"{baseUrl}/api/tournaments", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reach CaissaResults server at {Url}", serverUrl ?? GetBaseUrl());
            return false;
        }
    }

    public async Task<SyncResult> PublishTournamentAsync(Tournament tournament, CancellationToken cancellationToken = default)
    {
        if (!IsSyncConfigured)
        {
            return new SyncResult
            {
                Success = false,
                Message = "Upload disabled: CaissaResults API Key is not configured. Please enter the API key in Settings.",
                TournamentId = tournament.Id
            };
        }

        var baseUrl = GetBaseUrl();
        var transportError = GetInsecureTransportError(baseUrl);
        if (transportError != null)
        {
            _logger.LogWarning("Publish blocked: {Reason}", transportError);
            return new SyncResult { Success = false, Message = transportError, TournamentId = tournament.Id };
        }

        try
        {
            if (string.IsNullOrWhiteSpace(tournament.SyncKey))
            {
                tournament.SyncKey = CaissaSecurity.GenerateSecureKey(16);
            }

            var payload = new TournamentSyncPayload
            {
                Tournament = tournament,
                SyncKey = tournament.SyncKey,
                SyncedAt = DateTime.UtcNow,
                SourceClient = "CaissaDesk Workstation",
                IsDeltaUpdate = false
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var pathAndQuery = "/api/sync/publish";
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{pathAndQuery}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var apiKey = _settingsService.Settings.CaissaResultsApiKey;
            CaissaSecurity.SignRequest(request, pathAndQuery, json, apiKey, tournament.SyncKey);

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions, cancellationToken);
                return result ?? new SyncResult
                {
                    Success = true,
                    Message = "Tournament published successfully.",
                    TournamentId = tournament.Id
                };
            }

            var errorText = ScoutingHttp.SanitizeServerError(await response.Content.ReadAsStringAsync(cancellationToken));
            _logger.LogWarning("Publish of tournament {Id} failed with {StatusCode}: {Error}", tournament.Id, (int)response.StatusCode, errorText);
            return new SyncResult
            {
                Success = false,
                Message = $"Publish failed ({(int)response.StatusCode} {response.StatusCode}): {errorText}",
                TournamentId = tournament.Id
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while publishing tournament {Id} to CaissaResults", tournament.Id);
            return new SyncResult
            {
                Success = false,
                Message = $"Cannot connect to CaissaResults ({ex.Message}). Ensure CaissaResults is running.",
                TournamentId = tournament.Id
            };
        }
    }

    public async Task<SyncResult> SyncRoundDeltaAsync(string tournamentId, int roundNumber, List<Pairing> pairings, string? tournamentSyncKey = null, CancellationToken cancellationToken = default)
    {
        if (!IsSyncConfigured)
        {
            return new SyncResult
            {
                Success = false,
                Message = "Round sync disabled: CaissaResults API Key is not configured."
            };
        }

        var baseUrl = GetBaseUrl();
        var transportError = GetInsecureTransportError(baseUrl);
        if (transportError != null)
        {
            _logger.LogWarning("Round sync blocked: {Reason}", transportError);
            return new SyncResult { Success = false, Message = transportError };
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var pathAndQuery = $"/api/sync/round?tournamentId={Uri.EscapeDataString(tournamentId)}&roundNumber={roundNumber}";
            var json = JsonSerializer.Serialize(pairings, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{pathAndQuery}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var apiKey = _settingsService.Settings.CaissaResultsApiKey;
            CaissaSecurity.SignRequest(request, pathAndQuery, json, apiKey, tournamentSyncKey);

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions, cancellationToken);
                return result ?? new SyncResult { Success = true, Message = "Round updated." };
            }

            var errorText = ScoutingHttp.SanitizeServerError(await response.Content.ReadAsStringAsync(cancellationToken));
            _logger.LogWarning("Round {Round} sync for tournament {Id} failed with {StatusCode}: {Error}", roundNumber, tournamentId, (int)response.StatusCode, errorText);
            return new SyncResult
            {
                Success = false,
                Message = $"Round sync failed ({(int)response.StatusCode} {response.StatusCode}): {errorText}"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync round delta for tournament {Id}", tournamentId);
            return new SyncResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<bool> DeleteTournamentAsync(string tournamentId, string? tournamentSyncKey = null, CancellationToken cancellationToken = default)
    {
        if (!IsSyncConfigured)
        {
            _logger.LogWarning("Delete disabled: CaissaResults API Key is not configured.");
            return false;
        }

        var baseUrl = GetBaseUrl();
        var transportError = GetInsecureTransportError(baseUrl);
        if (transportError != null)
        {
            _logger.LogWarning("Delete blocked: {Reason}", transportError);
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var pathAndQuery = $"/api/tournaments/{Uri.EscapeDataString(tournamentId)}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{baseUrl}{pathAndQuery}");

            var apiKey = _settingsService.Settings.CaissaResultsApiKey;
            CaissaSecurity.SignRequest(request, pathAndQuery, string.Empty, apiKey, tournamentSyncKey);

            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete tournament {Id} on CaissaResults", tournamentId);
            return false;
        }
    }
}
