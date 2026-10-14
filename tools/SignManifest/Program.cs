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
    sequence = 67,
    version = "1.9.0",
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
    changelog = "- feat(relay): Fissal Relay Prime v1.9.0 full production overhaul\n- perf(client): eliminate 10Hz WS_EX_COMPOSITED idle repaints, 5Hz polling, and GDI+ mesh/handle leaks\n- perf(addon): O(1) rank lookups, cached bump/raffle telemetry, and zero-keystroke-lag MotD Studio\n- fix(addon): step-based GuildPromote/GuildDemote Auto-Ranks, probation logic fix, and Bumper drag/close fix\n- security(gateway): strict witness consensus, bounds validation, multi-account Lua parsing, and SyncPoints attribution"
};

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(manifestObj, jsonOpts);
var manifestBytes = Encoding.UTF8.GetBytes(json);

var privateKeyBytes = Convert.FromBase64String("wfOnpQt7X74D4ucVuSrBk6RBxzeDBsppnhkD9SfcSH8=");
var alg = SignatureAlgorithm.Ed25519;
using var key = Key.Import(alg, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
var signature = alg.Sign(key, manifestBytes);

var (ok, parsed, err) = UpdateTrustVerifier.VerifyAndParse(manifestBytes, signature, lastVerifiedSequence: 66);
if (!ok || parsed == null)
{
    Console.Error.WriteLine($"Verification failed: {err}");
    Environment.Exit(1);
}

Console.WriteLine($"✓ Cryptographic verification passed! Version: {parsed.Version}, Sequence: {parsed.Sequence}");

File.WriteAllBytes("/home/echo/DiscordBots/RedfurBot/updates/update-manifest.json", manifestBytes);
File.WriteAllBytes("/home/echo/DiscordBots/RedfurBot/updates/update-manifest.sig", signature);
Console.WriteLine("✓ Successfully wrote and signed update-manifest.json and update-manifest.sig!");
