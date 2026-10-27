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
    sequence = 76,
    version = "1.9.5",
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
    changelog = "- fix(bumper): Alt-Tab Safe Mode (Borderless Fullscreen auto-switch) & focus-loss-proof 250ms state machine with retry watchdog\n- feat(recon): alliance-split & adaptive Guild Finder passes, priority guild ID prober + 45s background trickle, and drive-by reticle buffer\n- feat(ui): Ground Recon Vault status filter pills ([All], [Verified], [Unverified], [Vacant ⭐], [Refuges]) & live search box\n- feat(sync): two-way fleet kiosk & 701+ known trading guild registry sync via FissalRelay_RaffleData.lua"
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
