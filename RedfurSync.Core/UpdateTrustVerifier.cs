using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace RedfurSync
{
    public sealed class UpdateFileArtifact
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    public sealed class KeyRotationPayload
    {
        [JsonPropertyName("add")]
        public List<string> Add { get; set; } = new();

        [JsonPropertyName("revoke")]
        public List<string> Revoke { get; set; } = new();
    }

    public sealed class UpdateManifest
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; } = 1;

        [JsonPropertyName("product")]
        public string Product { get; set; } = "fissal-relay";

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "stable";

        [JsonPropertyName("sequence")]
        public long Sequence { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("issued_at")]
        public DateTimeOffset IssuedAt { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonPropertyName("min_updater_version")]
        public string MinUpdaterVersion { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<UpdateFileArtifact> Files { get; set; } = new();

        [JsonPropertyName("key_rotation")]
        public KeyRotationPayload? KeyRotation { get; set; }

        [JsonPropertyName("revoked_keys")]
        public List<string> RevokedKeys { get; set; } = new();
    }

    /// <summary>
    /// Cryptographic trust engine for Fissal Relay updates adhering to Fable 5.1 ruling:
    /// - Ed25519 detached signature verification over raw manifest bytes
    /// - Verify-then-parse pipeline
    /// - Anti-downgrade monotonic sequence protection
    /// - Clock-skew tolerant expiry check (72h)
    /// - Two pinned embedded public keys (PRIMARY and RECOVERY)
    /// </summary>
    public static class UpdateTrustVerifier
    {
        public const string PrimaryPublicKeyBase64 = "Hm2nHWon0NlLPFFSvdn5e+MrOZhEqx/BTybr7OWnZFo=";
        public const string RecoveryPublicKeyBase64 = "wyhdxHRTHL/0OHYsAyI0JY9bm8V+qg0uATsLuDuLtsk=";

        public static readonly byte[] PrimaryPublicKey = Convert.FromBase64String(PrimaryPublicKeyBase64);
        public static readonly byte[] RecoveryPublicKey = Convert.FromBase64String(RecoveryPublicKeyBase64);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static string ComputeKeyId(byte[] rawPublicKey)
        {
            var hash = SHA256.HashData(rawPublicKey);
            return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }

        public static (bool Ok, UpdateManifest? Manifest, string Error) VerifyAndParse(
            byte[] rawManifestBytes,
            byte[] rawSignatureBytes,
            IEnumerable<byte[]>? extraTrustedKeys = null,
            IEnumerable<string>? revokedKeyIds = null,
            long lastVerifiedSequence = 0,
            DateTimeOffset? utcNow = null)
        {
            if (rawManifestBytes == null || rawManifestBytes.Length == 0)
                return (false, null, "Manifest bytes are empty.");
            if (rawSignatureBytes == null || rawSignatureBytes.Length != 64)
                return (false, null, "Signature must be 64 bytes Ed25519.");

            var revoked = new HashSet<string>(revokedKeyIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var trustedKeys = new List<byte[]> { PrimaryPublicKey, RecoveryPublicKey };
            if (extraTrustedKeys != null)
                trustedKeys.AddRange(extraTrustedKeys);

            var validKeys = trustedKeys.Where(k => !revoked.Contains(ComputeKeyId(k))).ToList();
            if (validKeys.Count == 0)
                return (false, null, "All available public keys are revoked.");

            var alg = SignatureAlgorithm.Ed25519;
            bool verified = false;

            foreach (var keyBytes in validKeys)
            {
                try
                {
                    var pubKey = PublicKey.Import(alg, keyBytes, KeyBlobFormat.RawPublicKey);
                    if (alg.Verify(pubKey, rawManifestBytes, rawSignatureBytes))
                    {
                        verified = true;
                        break;
                    }
                }
                catch
                {
                    // Continue checking other candidate keys
                }
            }

            if (!verified)
                return (false, null, "Ed25519 signature verification failed across all trusted keys.");

            // Verify-then-parse: only parse JSON after cryptographic proof
            UpdateManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<UpdateManifest>(rawManifestBytes, JsonOpts);
            }
            catch (Exception ex)
            {
                return (false, null, $"Manifest parse error: {ex.Message}");
            }

            if (manifest == null)
                return (false, null, "Manifest JSON deserialized to null.");

            if (manifest.Schema != 1)
                return (false, null, $"Unsupported manifest schema: {manifest.Schema}");

            // Anti-downgrade check
            if (manifest.Sequence <= lastVerifiedSequence)
                return (false, null, $"Manifest sequence {manifest.Sequence} is not strictly greater than last verified sequence {lastVerifiedSequence} (anti-downgrade violation).");

            // Freshness check with 72h clock skew tolerance
            var now = utcNow ?? DateTimeOffset.UtcNow;
            if (manifest.ExpiresAt != default && now > manifest.ExpiresAt.AddHours(72))
                return (false, null, $"Manifest expired at {manifest.ExpiresAt:O} (current time {now:O} exceeds 72h skew tolerance).");

            return (true, manifest, string.Empty);
        }
    }
}
