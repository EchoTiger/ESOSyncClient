using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using NSec.Cryptography;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

public sealed class UpdateTrustAndHandshakeTests
{
    private static readonly byte[] PrimaryPrivateKey = Convert.FromBase64String("wfOnpQt7X74D4ucVuSrBk6RBxzeDBsppnhkD9SfcSH8=");
    private static readonly byte[] RecoveryPrivateKey = Convert.FromBase64String("OEfjRiW/601338IYND7YdyR3a4EJdGv8uB2yAorYQ78=");

    private static byte[] SignWithPrimary(byte[] data)
    {
        var alg = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(alg, PrimaryPrivateKey, KeyBlobFormat.RawPrivateKey);
        return alg.Sign(key, data);
    }

    private static byte[] SignWithRecovery(byte[] data)
    {
        var alg = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(alg, RecoveryPrivateKey, KeyBlobFormat.RawPrivateKey);
        return alg.Sign(key, data);
    }

    private static string CreateValidManifestJson(long sequence = 48, string version = "1.5.0", DateTimeOffset? expiresAt = null)
    {
        var manifest = new UpdateManifest
        {
            Schema = 1,
            Product = "fissal-relay",
            Channel = "stable",
            Sequence = sequence,
            Version = version,
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(30),
            MinUpdaterVersion = "1.4.0",
            Files = new List<UpdateFileArtifact>
            {
                new()
                {
                    Path = "RedfurSync.exe",
                    Sha256 = new string('a', 64),
                    Size = 1048576,
                    Url = "https://redfur.ech-o.net/download/v1.5.0/RedfurSync.exe"
                }
            }
        };
        return JsonSerializer.Serialize(manifest);
    }

    [Fact]
    public void PinnedKeyIds_MatchExpectedIdentifiers()
    {
        string priId = UpdateTrustVerifier.ComputeKeyId(UpdateTrustVerifier.PrimaryPublicKey);
        string recId = UpdateTrustVerifier.ComputeKeyId(UpdateTrustVerifier.RecoveryPublicKey);

        Assert.Equal("3313dc36e41f5ba5", priId);
        Assert.Equal("a2ce372539a50cfd", recId);
    }

    [Fact]
    public void PrimaryKeySignedManifest_Succeeds()
    {
        var json = CreateValidManifestJson(sequence: 50);
        var bytes = Encoding.UTF8.GetBytes(json);
        var sig = SignWithPrimary(bytes);

        var (ok, manifest, error) = UpdateTrustVerifier.VerifyAndParse(
            bytes, sig, lastVerifiedSequence: 40);

        Assert.True(ok, error);
        Assert.NotNull(manifest);
        Assert.Equal(50, manifest.Sequence);
        Assert.Equal("1.5.0", manifest.Version);
        Assert.Single(manifest.Files);
    }

    [Fact]
    public void RecoveryKeySignedManifest_Succeeds()
    {
        var json = CreateValidManifestJson(sequence: 51);
        var bytes = Encoding.UTF8.GetBytes(json);
        var sig = SignWithRecovery(bytes);

        var (ok, manifest, error) = UpdateTrustVerifier.VerifyAndParse(
            bytes, sig, lastVerifiedSequence: 40);

        Assert.True(ok, error);
        Assert.NotNull(manifest);
        Assert.Equal(51, manifest.Sequence);
    }

    [Fact]
    public void CorruptedSignature_FailsVerificationBeforeJsonParse()
    {
        var json = CreateValidManifestJson(sequence: 52);
        var bytes = Encoding.UTF8.GetBytes(json);
        var sig = SignWithPrimary(bytes);
        sig[0] ^= 0xFF; // Corrupt signature byte

        var (ok, manifest, error) = UpdateTrustVerifier.VerifyAndParse(
            bytes, sig, lastVerifiedSequence: 40);

        Assert.False(ok);
        Assert.Null(manifest);
        Assert.Contains("Ed25519 signature verification failed", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TamperedManifestBytes_FailsVerification()
    {
        var json = CreateValidManifestJson(sequence: 53);
        var bytes = Encoding.UTF8.GetBytes(json);
        var sig = SignWithPrimary(bytes);

        // Tamper with payload
        bytes = Encoding.UTF8.GetBytes(json.Replace("53", "99"));

        var (ok, manifest, error) = UpdateTrustVerifier.VerifyAndParse(
            bytes, sig, lastVerifiedSequence: 40);

        Assert.False(ok);
        Assert.Null(manifest);
        Assert.Contains("Ed25519 signature verification failed", error, StringComparison.Ordinal);
    }

    [Fact]
    public void SequenceEqualOrLowerThanLastVerified_IsRejectedAsAntiDowngradeViolation()
    {
        var json = CreateValidManifestJson(sequence: 40);
        var bytes = Encoding.UTF8.GetBytes(json);
        var sig = SignWithPrimary(bytes);

        var (ok, manifest, error) = UpdateTrustVerifier.VerifyAndParse(
            bytes, sig, lastVerifiedSequence: 40);

        Assert.False(ok);
        Assert.Null(manifest);
        Assert.Contains("anti-downgrade violation", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiredManifestPastClockSkew_IsRejected()
    {
        var expiredTime = DateTimeOffset.UtcNow.AddHours(-100);
        var json = CreateValidManifestJson(sequence: 60, expiresAt: expiredTime);
        var bytes = Encoding.UTF8.GetBytes(json);
        var sig = SignWithPrimary(bytes);

        var (ok, manifest, error) = UpdateTrustVerifier.VerifyAndParse(
            bytes, sig, lastVerifiedSequence: 40);

        Assert.False(ok);
        Assert.Null(manifest);
        Assert.Contains("Manifest expired", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RevokedKey_IsRejectedEvenIfSignatureValid()
    {
        var json = CreateValidManifestJson(sequence: 70);
        var bytes = Encoding.UTF8.GetBytes(json);
        var sig = SignWithPrimary(bytes);

        string priId = UpdateTrustVerifier.ComputeKeyId(UpdateTrustVerifier.PrimaryPublicKey);

        var (ok, manifest, error) = UpdateTrustVerifier.VerifyAndParse(
            bytes, sig, revokedKeyIds: new[] { priId }, lastVerifiedSequence: 40);

        Assert.False(ok);
        Assert.Null(manifest);
        Assert.Contains("signature verification failed", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyWithHandshake_HealthySentinel_CompletesSuccessfully()
    {
        var fs = new InMemoryTestFileSystem();
        fs.WriteAllText("/app/RedfurSync.exe", "ORIGINAL_BINARY");
        fs.WriteAllText("/stage/Update.tmp", "NEW_BINARY");

        var installer = new UpdateInstaller(fs, path =>
        {
            // Simulate new process launching and writing HEALTHY sentinel
            fs.WriteAllText("/app/HEALTHY", "HEALTHY 2026-09-19T00:00:00Z");
        });

        var result = installer.ApplyWithHandshake(
            "/app/RedfurSync.exe",
            "/stage/Update.tmp",
            sequence: 50,
            fromVersion: "1.4.8",
            toVersion: "1.5.0",
            handshakeTimeout: TimeSpan.FromSeconds(2));

        Assert.True(result.Ok, result.Message);
        Assert.Equal("NEW_BINARY", fs.ReadAllText("/app/RedfurSync.exe"));
        Assert.False(fs.FileExists("/app/RedfurSync.exe.prev"));
        Assert.False(fs.FileExists("/app/pending.json"));
        Assert.False(fs.FileExists("/app/HEALTHY"));
    }

    [Fact]
    public void ApplyWithHandshake_TimeoutWithoutHealthy_RollsBackOriginal()
    {
        var fs = new InMemoryTestFileSystem();
        fs.WriteAllText("/app/RedfurSync.exe", "ORIGINAL_BINARY");
        fs.WriteAllText("/stage/Update.tmp", "NEW_BINARY");

        var installer = new UpdateInstaller(fs, _ =>
        {
            // Simulate process launch failure (never writes HEALTHY)
        });

        var result = installer.ApplyWithHandshake(
            "/app/RedfurSync.exe",
            "/stage/Update.tmp",
            sequence: 50,
            fromVersion: "1.4.8",
            toVersion: "1.5.0",
            handshakeTimeout: TimeSpan.FromMilliseconds(200));

        Assert.False(result.Ok);
        Assert.Contains("failed to confirm health", result.Message, StringComparison.Ordinal);
        // Original binary restored
        Assert.Equal("ORIGINAL_BINARY", fs.ReadAllText("/app/RedfurSync.exe"));
        Assert.False(fs.FileExists("/app/RedfurSync.exe.prev"));
        // pending.json reflects rolled_back
        Assert.True(fs.FileExists("/app/pending.json"));
        var pendingJson = fs.ReadAllText("/app/pending.json");
        Assert.Contains("rolled_back", pendingJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverPendingUpdate_CrashedBeforeHealthy_RestoresPrevFile()
    {
        var fs = new InMemoryTestFileSystem();
        fs.WriteAllText("/app/RedfurSync.exe", "CORRUPTED_NEW_BINARY");
        fs.WriteAllText("/app/RedfurSync.exe.prev", "RECOVERED_ORIGINAL_BINARY");
        fs.WriteAllText("/app/pending.json", JsonSerializer.Serialize(new PendingUpdateRecord
        {
            Sequence = 55,
            State = "applying",
            FromVersion = "1.4.8",
            ToVersion = "1.5.0"
        }));

        UpdateInstaller.RecoverPendingUpdate("/app", fs);

        Assert.Equal("RECOVERED_ORIGINAL_BINARY", fs.ReadAllText("/app/RedfurSync.exe"));
        Assert.False(fs.FileExists("/app/RedfurSync.exe.prev"));
        var pending = JsonSerializer.Deserialize<PendingUpdateRecord>(fs.ReadAllText("/app/pending.json"));
        Assert.NotNull(pending);
        Assert.Equal("recovered_rollback", pending.State);
    }

    private sealed class InMemoryTestFileSystem : IUpdateFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private static string Norm(string p) => p.Replace('\\', '/');

        public bool FileExists(string path) => _files.ContainsKey(Norm(path));

        public void DeleteFile(string path) => _files.Remove(Norm(path));

        public void MoveFile(string sourcePath, string destinationPath)
        {
            if (!_files.TryGetValue(Norm(sourcePath), out var content))
                throw new FileNotFoundException("Source not found", sourcePath);
            _files.Remove(Norm(sourcePath));
            _files[Norm(destinationPath)] = content;
        }

        public string ReadAllText(string path)
        {
            if (_files.TryGetValue(Norm(path), out var content)) return content;
            throw new FileNotFoundException("File not found", path);
        }

        public void WriteAllText(string path, string content)
        {
            _files[Norm(path)] = content;
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        {
            string cleanDir = Norm(path).TrimEnd('/');
            foreach (var key in _files.Keys.ToList())
            {
                if (key.StartsWith(cleanDir + "/", StringComparison.OrdinalIgnoreCase))
                {
                    if (searchPattern == "*.prev" && key.EndsWith(".prev", StringComparison.OrdinalIgnoreCase))
                        yield return key;
                }
            }
        }
    }
}
