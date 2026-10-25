using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using RedfurSync;

var exePath = "/home/echo/DiscordBots/RedfurBot/updates/RedfurSync.exe";
if (!File.Exists(exePath))
{
    Console.Error.WriteLine($"File not found: {exePath}");
    Environment.Exit(1);
}

var exeBytes = File.ReadAllBytes(exePath);
var exeHash = Convert.ToHexString(SHA256.HashData(exeBytes)).ToLowerInvariant();
var exeSize = (long)exeBytes.Length;

Console.WriteLine($"RedfurSync.exe size: {exeSize}, sha256: {exeHash}");

var manifestObj = new
{
    schema = 1,
    product = "fissal-relay",
    channel = "stable",
    sequence = 74,
    version = "1.9.3",
    issued_at = DateTimeOffset.UtcNow.ToString("O"),
    expires_at = DateTimeOffset.UtcNow.AddYears(1).ToString("O"),
    min_updater_version = "1.4.0",
    files = new[]
    {
        new
        {
            path = "RedfurSync.exe",
            sha256 = exeHash,
            size = exeSize,
            url = "https://redfur.ech-o.net/api/relay/v1/update-download"
        }
    },
    downloadUrl = "https://redfur.ech-o.net/api/relay/v1/update-download",
    sizeBytes = exeSize,
    sha256 = exeHash,
    changelog = "- feat(ui): clear Anonymous (Bank-Only) vs Verified Paired badges in nav rail, micro-display, and setup dialog\n- perf(addon): O(1) coalesced SavedVars priority-save debounce, memoized Kiosk Recon & Member Sales caches, and instant Roster Auditor sorting\n- fix(console): Redfur Caravan raffle overview support, dynamic label anchoring, and ESO font-safe rank/medal badges\n- perf(client): GDI+ font handle cache, inline Lua comment brace-lexer hardening, and 166 KB slim addon archive"
};

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(manifestObj, jsonOpts);
var manifestBytes = Encoding.UTF8.GetBytes(json);

var privateKeyBytes = Convert.FromBase64String("wfOnpQt7X74D4ucVuSrBk6RBxzeDBsppnhkD9SfcSH8=");
var alg = SignatureAlgorithm.Ed25519;
using var key = Key.Import(alg, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
var signature = alg.Sign(key, manifestBytes);

var (ok, parsed, err) = UpdateTrustVerifier.VerifyAndParse(manifestBytes, signature, lastVerifiedSequence: 70);
if (!ok || parsed == null)
{
    Console.Error.WriteLine($"Verification failed: {err}");
    Environment.Exit(1);
}

Console.WriteLine($"✓ Cryptographic verification passed! Version: {parsed.Version}, Sequence: {parsed.Sequence}");

File.WriteAllBytes("/home/echo/DiscordBots/RedfurBot/updates/update-manifest.json", manifestBytes);
File.WriteAllBytes("/home/echo/DiscordBots/RedfurBot/updates/update-manifest.sig", signature);
Console.WriteLine("✓ Successfully wrote and signed update-manifest.json and update-manifest.sig!");
