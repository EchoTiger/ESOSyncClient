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
    sequence = 70,
    version = "1.9.2",
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
    changelog = "- feat(anonymous): out-of-the-box anonymous sync mode (quarantine-tagged, non-Redfur guild bank only)\n- feat(pairing): 1-2-3 guided pairing checklist & automatic anonymous-to-verified device upgrade\n- fix(sync): remove TamrielTradeCentre folder watching (handled autonomously by server harvester)\n- fix(watcher): guarantee SavedVariables watcher & auto-updater start unconditionally + add Sync Now action"
};

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(manifestObj, jsonOpts);
var manifestBytes = Encoding.UTF8.GetBytes(json);

var privateKeyBytes = Convert.FromBase64String("wfOnpQt7X74D4ucVuSrBk6RBxzeDBsppnhkD9SfcSH8=");
var alg = SignatureAlgorithm.Ed25519;
using var key = Key.Import(alg, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
var signature = alg.Sign(key, manifestBytes);

var (ok, parsed, err) = UpdateTrustVerifier.VerifyAndParse(manifestBytes, signature, lastVerifiedSequence: 69);
if (!ok || parsed == null)
{
    Console.Error.WriteLine($"Verification failed: {err}");
    Environment.Exit(1);
}

Console.WriteLine($"✓ Cryptographic verification passed! Version: {parsed.Version}, Sequence: {parsed.Sequence}");

File.WriteAllBytes("/home/echo/DiscordBots/RedfurBot/updates/update-manifest.json", manifestBytes);
File.WriteAllBytes("/home/echo/DiscordBots/RedfurBot/updates/update-manifest.sig", signature);
Console.WriteLine("✓ Successfully wrote and signed update-manifest.json and update-manifest.sig!");
