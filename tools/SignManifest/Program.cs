using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using RedfurSync;

static string? GetArg(string[] args, string flag)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}

string signingKeyB64 = Environment.GetEnvironmentVariable("RELAY_SIGNING_PRIVATE_KEY_B64") ?? string.Empty;
if (string.IsNullOrWhiteSpace(signingKeyB64))
{
    Console.Error.WriteLine("Error: RELAY_SIGNING_PRIVATE_KEY_B64 environment variable is required (32-byte raw Ed25519 seed in Base64).");
    Environment.Exit(1);
}

var exePath = GetArg(args, "--exe")
    ?? Environment.GetEnvironmentVariable("RELAY_EXE_PATH")
    ?? "/home/echo/DiscordBots/RedfurBot/updates/RedfurSync.exe";

if (!File.Exists(exePath))
{
    Console.Error.WriteLine($"File not found: {exePath}");
    Environment.Exit(1);
}

var outDir = GetArg(args, "--out-dir")
    ?? Environment.GetEnvironmentVariable("RELAY_UPDATES_DIR")
    ?? Path.GetDirectoryName(Path.GetFullPath(exePath))
    ?? ".";

var version = GetArg(args, "--version")
    ?? Environment.GetEnvironmentVariable("RELAY_VERSION")
    ?? RelayVersion.Current;

var seqStr = GetArg(args, "--sequence")
    ?? Environment.GetEnvironmentVariable("RELAY_SEQUENCE");

long sequence;
if (!string.IsNullOrWhiteSpace(seqStr))
{
    sequence = long.Parse(seqStr, CultureInfo.InvariantCulture);
}
else
{
    var existingManifestPath = Path.Combine(outDir, "update-manifest.json");
    if (File.Exists(existingManifestPath))
    {
        using var existingDoc = JsonDocument.Parse(File.ReadAllText(existingManifestPath));
        sequence = existingDoc.RootElement.TryGetProperty("sequence", out var seqEl) ? seqEl.GetInt64() + 1 : 1;
    }
    else
    {
        sequence = 1;
    }
}

var downloadUrl = GetArg(args, "--download-url")
    ?? Environment.GetEnvironmentVariable("RELAY_DOWNLOAD_URL")
    ?? "https://redfur.ech-o.net/api/relay/v1/update-download";

var minUpdaterVersion = GetArg(args, "--min-updater-version")
    ?? Environment.GetEnvironmentVariable("RELAY_MIN_UPDATER_VERSION")
    ?? "1.4.0";

var expiryDaysStr = GetArg(args, "--expiry-days")
    ?? Environment.GetEnvironmentVariable("RELAY_EXPIRY_DAYS")
    ?? "90";
int expiryDays = int.TryParse(expiryDaysStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDays) && parsedDays > 0
    ? parsedDays
    : 90;

string? changelogPath = GetArg(args, "--changelog-file");
string changelog = !string.IsNullOrWhiteSpace(changelogPath) && File.Exists(changelogPath)
    ? File.ReadAllText(changelogPath).Trim()
    : (GetArg(args, "--changelog")
        ?? Environment.GetEnvironmentVariable("RELAY_CHANGELOG")
        ?? $"Fissal Relay v{version}");

var addKeysCsv = GetArg(args, "--rotate-add") ?? Environment.GetEnvironmentVariable("RELAY_ROTATE_ADD_B64");
var revokeKeysCsv = GetArg(args, "--rotate-revoke") ?? Environment.GetEnvironmentVariable("RELAY_ROTATE_REVOKE_IDS");

var rotateAdd = new List<string>();
if (!string.IsNullOrWhiteSpace(addKeysCsv))
{
    foreach (var part in addKeysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        rotateAdd.Add(part);
}

var rotateRevoke = new List<string>();
if (!string.IsNullOrWhiteSpace(revokeKeysCsv))
{
    foreach (var part in revokeKeysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        rotateRevoke.Add(part.ToLowerInvariant());
}

var exeBytes = File.ReadAllBytes(exePath);
var exeHash = Convert.ToHexString(SHA256.HashData(exeBytes)).ToLowerInvariant();
var exeSize = (long)exeBytes.Length;

Console.WriteLine($"RedfurSync.exe size: {exeSize}, sha256: {exeHash}");

var manifestObj = new Dictionary<string, object?>
{
    ["schema"] = 1,
    ["product"] = "fissal-relay",
    ["channel"] = "stable",
    ["sequence"] = sequence,
    ["version"] = version,
    ["issued_at"] = DateTimeOffset.UtcNow.ToString("O"),
    ["expires_at"] = DateTimeOffset.UtcNow.AddDays(expiryDays).ToString("O"),
    ["min_updater_version"] = minUpdaterVersion,
    ["files"] = new[]
    {
        new
        {
            path = "RedfurSync.exe",
            sha256 = exeHash,
            size = exeSize,
            url = downloadUrl
        }
    },
    ["downloadUrl"] = downloadUrl,
    ["sizeBytes"] = exeSize,
    ["sha256"] = exeHash,
    ["changelog"] = changelog
};

if (rotateAdd.Count > 0 || rotateRevoke.Count > 0)
{
    manifestObj["key_rotation"] = new
    {
        add = rotateAdd,
        revoke = rotateRevoke
    };
}

if (rotateRevoke.Count > 0)
{
    manifestObj["revoked_keys"] = rotateRevoke;
}

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(manifestObj, jsonOpts);
var manifestBytes = Encoding.UTF8.GetBytes(json);

var privateKeyBytes = Convert.FromBase64String(signingKeyB64.Trim());
var alg = SignatureAlgorithm.Ed25519;
using var key = Key.Import(alg, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
var pubKeyBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
var keyId = UpdateTrustVerifier.ComputeKeyId(pubKeyBytes);
var signature = alg.Sign(key, manifestBytes);

var (ok, parsed, err) = UpdateTrustVerifier.VerifyAndParse(manifestBytes, signature, lastVerifiedSequence: sequence);
if (!ok || parsed == null)
{
    Console.Error.WriteLine($"Verification failed (signer keyId={keyId}): {err}");
    Environment.Exit(1);
}

Console.WriteLine($"✓ Cryptographic verification passed! Signer keyId: {keyId}, Version: {parsed.Version}, Sequence: {parsed.Sequence}");

Directory.CreateDirectory(outDir);
var outManifestPath = Path.Combine(outDir, "update-manifest.json");
var outSigPath = Path.Combine(outDir, "update-manifest.sig");
File.WriteAllBytes(outManifestPath, manifestBytes);
File.WriteAllBytes(outSigPath, signature);
Console.WriteLine($"✓ Wrote {outManifestPath} and {outSigPath}!");
