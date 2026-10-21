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
        return $"{GetBaseUrl()}/tournament/{tournamentId}";
    }

    public async Task<bool> PingServerAsync(string? serverUrl = null)
    {
        try
        {
            var baseUrl = serverUrl?.TrimEnd('/') ?? GetBaseUrl();
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var response = await client.GetAsync($"{baseUrl}/api/tournaments");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reach CaissaResults server at {Url}", serverUrl ?? GetBaseUrl());
            return false;
        }
    }

    public async Task<SyncResult> PublishTournamentAsync(Tournament tournament)
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

        try
        {
            if (string.IsNullOrWhiteSpace(tournament.SyncKey))
            {
                tournament.SyncKey = CaissaSecurity.GenerateSecureKey(16);
            }

            var baseUrl = GetBaseUrl();
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
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{pathAndQuery}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var apiKey = _settingsService.Settings.CaissaResultsApiKey;
            CaissaSecurity.SignRequest(request, pathAndQuery, json, apiKey, tournament.SyncKey);

            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
                return result ?? new SyncResult
                {
                    Success = true,
                    Message = "Tournament published successfully.",
                    TournamentId = tournament.Id
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new SyncResult
                {
                    Success = false,
                    Message = $"Publish failed ({response.StatusCode}): {errorText}",
                    TournamentId = tournament.Id
                };
            }
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

    public async Task<SyncResult> SyncRoundDeltaAsync(string tournamentId, int roundNumber, List<Pairing> pairings, string? tournamentSyncKey = null)
    {
        if (!IsSyncConfigured)
        {
            return new SyncResult
            {
                Success = false,
                Message = "Round sync disabled: CaissaResults API Key is not configured."
            };
        }

        try
        {
            var baseUrl = GetBaseUrl();
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var pathAndQuery = $"/api/sync/round?tournamentId={tournamentId}&roundNumber={roundNumber}";
            var json = JsonSerializer.Serialize(pairings, JsonOptions);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{pathAndQuery}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var apiKey = _settingsService.Settings.CaissaResultsApiKey;
            CaissaSecurity.SignRequest(request, pathAndQuery, json, apiKey, tournamentSyncKey);

            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
                return result ?? new SyncResult { Success = true, Message = "Round updated." };
            }

            var errorText = await response.Content.ReadAsStringAsync();
            return new SyncResult
            {
                Success = false,
                Message = $"Round sync failed ({response.StatusCode}): {errorText}"
            };
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

    public async Task<bool> DeleteTournamentAsync(string tournamentId, string? tournamentSyncKey = null)
    {
        if (!IsSyncConfigured)
        {
            _logger.LogWarning("Delete disabled: CaissaResults API Key is not configured.");
            return false;
        }

        try
        {
            var baseUrl = GetBaseUrl();
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var pathAndQuery = $"/api/tournaments/{tournamentId}";
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{baseUrl}{pathAndQuery}");

            var apiKey = _settingsService.Settings.CaissaResultsApiKey;
            CaissaSecurity.SignRequest(request, pathAndQuery, string.Empty, apiKey, tournamentSyncKey);

            var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete tournament {Id} on CaissaResults", tournamentId);
            return false;
        }
    }
}
