using System.Security.Cryptography;
using System.Text;

namespace Caissa.Shared.Security;

public static class CaissaSecurity
{
    public const string HeaderTimestamp = "X-Caissa-Timestamp";
    public const string HeaderSignature = "X-Caissa-Signature";
    public const string HeaderTournamentKey = "X-Caissa-Tournament-Key";

    /// <summary>Single-use random value that lets the server reject replayed requests.</summary>
    public const string HeaderNonce = "X-Caissa-Nonce";

    /// <summary>
    /// HMAC over the v1 string-to-sign plus the nonce. Sent alongside the v1 <see cref="HeaderSignature"/>
    /// so servers that don't know about nonces keep accepting requests.
    /// </summary>
    public const string HeaderSignatureV2 = "X-Caissa-Signature-V2";

    /// <summary>
    /// Computes the SHA-256 hash of a string, returning a lowercase hex string.
    /// </summary>
    public static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        return ComputeSha256(bytes);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a byte array, returning a lowercase hex string.
    /// </summary>
    public static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the HMAC-SHA256 signature for a message using the given secret key.
    /// </summary>
    public static string ComputeHmacSha256(string secretKey, string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey ?? string.Empty);
        var messageBytes = Encoding.UTF8.GetBytes(message ?? string.Empty);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(messageBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time comparison between two strings to prevent timing attacks.
    /// </summary>
    public static bool FixedTimeEquals(string? a, string? b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }

    /// <summary>
    /// Constructs a standardized canonical string representation for HMAC request signing.
    /// </summary>
    public static string BuildStringToSign(string method, string pathAndQuery, string timestamp, string bodySha256)
    {
        var normalizedMethod = (method ?? "POST").Trim().ToUpperInvariant();
        var normalizedPath = (pathAndQuery ?? string.Empty).Trim();
        var normalizedTimestamp = (timestamp ?? string.Empty).Trim();
        var normalizedHash = (bodySha256 ?? string.Empty).Trim().ToLowerInvariant();

        return $"{normalizedTimestamp}\n{normalizedMethod}\n{normalizedPath}\n{normalizedHash}";
    }

    /// <summary>
    /// String-to-sign for <see cref="HeaderSignatureV2"/>: the v1 canonical string followed by the nonce.
    /// </summary>
    public static string BuildStringToSignV2(string method, string pathAndQuery, string timestamp, string bodySha256, string nonce)
    {
        return $"{BuildStringToSign(method, pathAndQuery, timestamp, bodySha256)}\n{(nonce ?? string.Empty).Trim()}";
    }

    /// <summary>
    /// Validates whether a provided key matches a stored key.
    /// Supports both SHA-256 hashed keys (recommended) and legacy plaintext keys.
    /// </summary>
    public static bool VerifyTournamentKey(string storedKeyOrHash, string providedKey)
    {
        if (string.IsNullOrEmpty(storedKeyOrHash) || string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        // 1. Check against SHA-256 hash of provided key (standard)
        var providedHash = ComputeSha256(providedKey);
        if (FixedTimeEquals(storedKeyOrHash, providedHash))
        {
            return true;
        }

        // 2. Fallback for legacy unhashed stored keys. A stored value that is itself a SHA-256 hash is never
        // compared directly, otherwise anyone holding the hash (e.g. from a database backup) could use it as the key.
        if (IsSha256Hex(storedKeyOrHash))
        {
            return false;
        }

        return FixedTimeEquals(storedKeyOrHash, providedKey);
    }

    private static bool IsSha256Hex(string value)
    {
        return value.Length == 64 && value.All(Uri.IsHexDigit);
    }

    /// <summary>
    /// Generates a cryptographically secure random alphanumeric hex key.
    /// </summary>
    public static string GenerateSecureKey(int byteLength = 16)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Signs an outgoing HTTP request with timestamp, body hash, and HMAC-SHA256 signature.
    /// </summary>
    /// <param name="includeBearerToken">
    /// Also send the API key as a Bearer token. The signature alone authenticates the request, and the server
    /// only falls back to the Bearer token for unsigned requests, so clients should pass <c>false</c> to keep
    /// the HMAC secret off the wire.
    /// </param>
    public static void SignRequest(
        HttpRequestMessage request,
        string pathAndQuery,
        string? bodyJson,
        string? apiKey,
        string? tournamentKey = null,
        bool includeBearerToken = true)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyHash = ComputeSha256(bodyJson ?? string.Empty);
        var method = request.Method.Method;

        request.Headers.Remove(HeaderTimestamp);
        request.Headers.Add(HeaderTimestamp, timestamp);

        if (!string.IsNullOrWhiteSpace(tournamentKey))
        {
            request.Headers.Remove(HeaderTournamentKey);
            request.Headers.Add(HeaderTournamentKey, tournamentKey);
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (includeBearerToken)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            var stringToSign = BuildStringToSign(method, pathAndQuery, timestamp, bodyHash);
            var signature = ComputeHmacSha256(apiKey, stringToSign);

            request.Headers.Remove(HeaderSignature);
            request.Headers.Add(HeaderSignature, signature);

            var nonce = GenerateSecureKey(16);
            var signatureV2 = ComputeHmacSha256(apiKey, BuildStringToSignV2(method, pathAndQuery, timestamp, bodyHash, nonce));

            request.Headers.Remove(HeaderNonce);
            request.Headers.Add(HeaderNonce, nonce);
            request.Headers.Remove(HeaderSignatureV2);
            request.Headers.Add(HeaderSignatureV2, signatureV2);
        }
    }
}
