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
    sequence = 69,
    version = "1.9.1",
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
    changelog = "- fix(pairing): auto-upgrade legacy http://redfur.ech-o.net endpoints to HTTPS, allow re-pairing over stale tokens, and sanitize pasted 6-digit codes\n- fix(addon): anti-kick rate-limit pacing (1.85s + 4.2s breather every 8 steps) & background LibHistoire/KioskSweep pause during Auto-Ranks & Roster Audit\n- feat(ui): widened 960x650 Command Console with zero text overflow, >=6px tactile button spacing, and ellipsis column bounds\n- fix(ui): recursive DT_HIGH popup/drawer z-order engine preventing popups from falling behind Console after closing once\n- feat(ui): Clockwork Obsidian & Burnished Brass confirmation modal (FissalRelay_ConfirmModal) replacing raw text dialogs"
};

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(manifestObj, jsonOpts);
var manifestBytes = Encoding.UTF8.GetBytes(json);

var privateKeyBytes = Convert.FromBase64String("wfOnpQt7X74D4ucVuSrBk6RBxzeDBsppnhkD9SfcSH8=");
var alg = SignatureAlgorithm.Ed25519;
using var key = Key.Import(alg, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
var signature = alg.Sign(key, manifestBytes);

var (ok, parsed, err) = UpdateTrustVerifier.VerifyAndParse(manifestBytes, signature, lastVerifiedSequence: 68);
if (!ok || parsed == null)
{
    Console.Error.WriteLine($"Verification failed: {err}");
    Environment.Exit(1);
}

Console.WriteLine($"✓ Cryptographic verification passed! Version: {parsed.Version}, Sequence: {parsed.Sequence}");

File.WriteAllBytes("/home/echo/DiscordBots/RedfurBot/updates/update-manifest.json", manifestBytes);
File.WriteAllBytes("/home/echo/DiscordBots/RedfurBot/updates/update-manifest.sig", signature);
Console.WriteLine("✓ Successfully wrote and signed update-manifest.json and update-manifest.sig!");
