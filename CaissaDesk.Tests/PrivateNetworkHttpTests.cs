using System.Net;
using Caissa.Shared.Models;
using CaissaDesk.Core.Settings;
using CaissaDesk.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaissaDesk.Tests;

public class PrivateNetworkHttpTests
{
    [Theory]
    [InlineData("http://10.0.0.5:5200", true)]
    [InlineData("http://10.255.255.255", true)]
    [InlineData("http://172.16.0.1", true)]
    [InlineData("http://172.31.255.254", true)]
    [InlineData("http://172.15.0.1", false)]
    [InlineData("http://172.32.0.1", false)]
    [InlineData("http://192.168.1.10:5200", true)]
    [InlineData("http://192.169.1.10", false)]
    [InlineData("http://169.254.12.34", true)]
    [InlineData("http://169.253.0.1", false)]
    [InlineData("http://8.8.8.8", false)]
    [InlineData("http://11.0.0.1", false)]
    [InlineData("http://[fd12:3456:789a::1]:5200", true)]
    [InlineData("http://[fc00::1]", true)]
    [InlineData("http://[fe80::1]", true)]
    [InlineData("http://[febf::1]", true)]
    [InlineData("http://[fec0::1]", false)]
    [InlineData("http://[2001:db8::1]", false)]
    [InlineData("http://[::ffff:192.168.0.5]", true)]
    [InlineData("http://[::ffff:8.8.8.8]", false)]
    [InlineData("http://results-pc:5200", true)]
    [InlineData("http://caissaresults.local", true)]
    [InlineData("http://CaissaResults.LOCAL.", true)]
    [InlineData("http://results.example.org", false)]
    [InlineData("http://192.168.1.10.nip.io", false)]
    [InlineData("http://local", true)]
    [InlineData("http://notlocal.com", false)]
    public void IsPrivateNetworkHost_ClassifiesAddresses(string url, bool expected)
    {
        Assert.Equal(expected, CaissaResultsSyncService.IsPrivateNetworkHost(new Uri(url)));
    }

    [Theory]
    // Flag off: unchanged behaviour (only https or loopback http)
    [InlineData("http://192.168.1.10:5200", false, false)]
    [InlineData("http://results-pc", false, false)]
    [InlineData("http://localhost:5200", false, true)]
    [InlineData("https://results.example.org", false, true)]
    // Flag on: LAN http allowed, public http never
    [InlineData("http://192.168.1.10:5200", true, true)]
    [InlineData("http://10.1.2.3", true, true)]
    [InlineData("http://[fe80::1]", true, true)]
    [InlineData("http://results-pc", true, true)]
    [InlineData("http://venue.local:5200", true, true)]
    [InlineData("http://localhost:5200", true, true)]
    [InlineData("http://results.example.org", true, false)]
    [InlineData("http://8.8.8.8", true, false)]
    [InlineData("http://[2001:db8::1]", true, false)]
    [InlineData("https://results.example.org", true, true)]
    [InlineData("ftp://192.168.1.10", true, false)]
    [InlineData("not a url", true, false)]
    public void GetInsecureTransportError_HonoursPrivateNetworkOptIn(string url, bool allowLan, bool allowed)
    {
        var error = CaissaResultsSyncService.GetInsecureTransportError(url, allowLan);
        Assert.Equal(allowed, error == null);
    }

    [Fact]
    public void AppSettings_LanHttpIsOffByDefault()
    {
        Assert.False(new AppSettings().AllowHttpOnPrivateNetworks);
        Assert.True(new AppSettings { AllowHttpOnPrivateNetworks = true }.Clone().AllowHttpOnPrivateNetworks);
    }

    [Theory]
    [InlineData("http://192.168.1.10:5200", false, false)]
    [InlineData("http://192.168.1.10:5200", true, true)]
    [InlineData("http://results.example.org", true, false)]
    public async Task Publish_SendsSignedRequestOnlyWhenTransportAllowed(string url, bool allowLan, bool expectSent)
    {
        var sent = new List<HttpRequestMessage>();
        var handler = new RecordingHandler(sent);
        var settings = new FakeSettings(new AppSettings
        {
            CaissaResultsUrl = url,
            CaissaResultsApiKey = "test-api-key",
            AllowHttpOnPrivateNetworks = allowLan
        });
        var service = new CaissaResultsSyncService(new FakeFactory(handler), settings, NullLogger<CaissaResultsSyncService>.Instance);

        var result = await service.PublishTournamentAsync(new Tournament { Name = "LAN Open" });

        Assert.Equal(expectSent, sent.Count == 1);
        Assert.Equal(expectSent, result.Success);
        if (expectSent)
        {
            Assert.StartsWith(url.TrimEnd('/'), sent[0].RequestUri!.ToString());
        }
        else
        {
            Assert.Contains("unencrypted HTTP", result.Message);
        }

        // Round delta and delete go through the same gate
        sent.Clear();
        var round = await service.SyncRoundDeltaAsync("t1", 1, []);
        Assert.Equal(expectSent, round.Success);
        bool deleted = await service.DeleteTournamentAsync("t1");
        Assert.Equal(expectSent, deleted);
        Assert.Equal(expectSent ? 2 : 0, sent.Count);
    }

    private sealed class RecordingHandler(List<HttpRequestMessage> sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"message\":\"ok\"}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeSettings(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Settings { get; } = settings;
        public event Action? OnSettingsChanged { add { } remove { } }
        public Task LoadSettingsAsync() => Task.CompletedTask;
        public Task SaveSettingsAsync(AppSettings newSettings) => Task.CompletedTask;
    }
}
