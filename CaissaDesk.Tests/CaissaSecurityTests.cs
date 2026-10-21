using Caissa.Shared.Security;

namespace CaissaDesk.Tests;

public class CaissaSecurityTests
{
    private const string ApiKey = "test-api-key-123";

    [Fact]
    public void VerifyTournamentKey_AcceptsRawKeyAgainstStoredHash()
    {
        var rawKey = CaissaSecurity.GenerateSecureKey(16);
        var storedHash = CaissaSecurity.ComputeSha256(rawKey);

        Assert.True(CaissaSecurity.VerifyTournamentKey(storedHash, rawKey));
        Assert.False(CaissaSecurity.VerifyTournamentKey(storedHash, "wrong"));
    }

    [Fact]
    public void VerifyTournamentKey_RejectsStoredHashPresentedAsKey()
    {
        var rawKey = CaissaSecurity.GenerateSecureKey(16);
        var storedHash = CaissaSecurity.ComputeSha256(rawKey);

        // Someone who only has the hash (e.g. from a DB backup) must not be able to authenticate with it
        Assert.False(CaissaSecurity.VerifyTournamentKey(storedHash, storedHash));
        Assert.False(CaissaSecurity.VerifyTournamentKey(storedHash.ToUpperInvariant(), storedHash.ToUpperInvariant()));
    }

    [Fact]
    public void VerifyTournamentKey_StillAcceptsLegacyPlaintextKeys()
    {
        var legacyKey = Guid.NewGuid().ToString("N");

        Assert.True(CaissaSecurity.VerifyTournamentKey(legacyKey, legacyKey));
        Assert.True(CaissaSecurity.VerifyTournamentKey("legacy-plaintext-key", "legacy-plaintext-key"));
        Assert.False(CaissaSecurity.VerifyTournamentKey(legacyKey, "other"));
    }

    [Fact]
    public void SignRequest_WithoutBearer_SignsButDoesNotSendApiKey()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/api/sync/publish");

        CaissaSecurity.SignRequest(request, "/api/sync/publish", "{}", ApiKey, "tkey", includeBearerToken: false);

        Assert.Null(request.Headers.Authorization);
        Assert.True(request.Headers.Contains(CaissaSecurity.HeaderSignature));
        Assert.True(request.Headers.Contains(CaissaSecurity.HeaderTimestamp));

        var timestamp = request.Headers.GetValues(CaissaSecurity.HeaderTimestamp).Single();
        var expected = CaissaSecurity.ComputeHmacSha256(ApiKey,
            CaissaSecurity.BuildStringToSign("POST", "/api/sync/publish", timestamp, CaissaSecurity.ComputeSha256("{}")));
        Assert.Equal(expected, request.Headers.GetValues(CaissaSecurity.HeaderSignature).Single());
        Assert.DoesNotContain(request.Headers.SelectMany(h => h.Value), v => v.Contains(ApiKey));
    }

    [Fact]
    public void SignRequest_DefaultStillSendsBearerForCompatibility()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/x");

        CaissaSecurity.SignRequest(request, "/x", "{}", ApiKey);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(ApiKey, request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public void SignRequest_AddsNonceAndV2SignatureCoveringIt()
    {
        using var first = new HttpRequestMessage(HttpMethod.Post, "https://example.test/api/sync/publish");
        using var second = new HttpRequestMessage(HttpMethod.Post, "https://example.test/api/sync/publish");

        CaissaSecurity.SignRequest(first, "/api/sync/publish", "{}", ApiKey, includeBearerToken: false);
        CaissaSecurity.SignRequest(second, "/api/sync/publish", "{}", ApiKey, includeBearerToken: false);

        var nonce = first.Headers.GetValues(CaissaSecurity.HeaderNonce).Single();
        var timestamp = first.Headers.GetValues(CaissaSecurity.HeaderTimestamp).Single();
        var expectedV2 = CaissaSecurity.ComputeHmacSha256(ApiKey, CaissaSecurity.BuildStringToSignV2(
            "POST", "/api/sync/publish", timestamp, CaissaSecurity.ComputeSha256("{}"), nonce));

        Assert.Equal(32, nonce.Length);
        Assert.Equal(expectedV2, first.Headers.GetValues(CaissaSecurity.HeaderSignatureV2).Single());
        Assert.NotEqual(nonce, second.Headers.GetValues(CaissaSecurity.HeaderNonce).Single());

        // The v1 signature is unchanged, so servers without nonce support keep working
        var expectedV1 = CaissaSecurity.ComputeHmacSha256(ApiKey,
            CaissaSecurity.BuildStringToSign("POST", "/api/sync/publish", timestamp, CaissaSecurity.ComputeSha256("{}")));
        Assert.Equal(expectedV1, first.Headers.GetValues(CaissaSecurity.HeaderSignature).Single());
    }
}
