using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

/// Guards the three independent size checks in UploadService.DownloadUpdateAsync:
/// declared Content-Length bounds, Content-Length vs manifest, and streamed bytes vs manifest.
public sealed class UpdateDownloadValidationTests
{
    private const string ManifestUrl = "https://relay.invalid/api/relay/v1/update-manifest";
    private const string DownloadUrl = "https://relay.invalid/RedfurSync.exe";

    [Fact]
    public async Task ContentLengthBelowManifestSize_IsRejected()
    {
        var payload = Payload(512);
        var result = await DownloadAsync(payload, manifestSize: 1024, declaredLength: 512);

        Assert.False(result.Ok);
        Assert.Contains("Update size did not match the update manifest.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentLengthAboveAllowedLimit_IsRejected()
    {
        var oversize = (500L * 1024 * 1024) + 1;
        var result = await DownloadAsync(Payload(8), manifestSize: oversize, declaredLength: oversize);

        Assert.False(result.Ok);
        Assert.Contains("outside the allowed limit", result.Error, StringComparison.Ordinal);
        // Rejected on headers alone; the body must never be streamed to disk.
        Assert.Null(result.WrittenBytes);
    }

    [Fact]
    public async Task ZeroContentLength_IsRejected()
    {
        var result = await DownloadAsync(Array.Empty<byte>(), manifestSize: 0, declaredLength: 0);

        Assert.False(result.Ok);
        Assert.Contains("outside the allowed limit", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChunkedResponseShorterThanManifest_IsRejected()
    {
        var result = await DownloadAsync(Payload(900), manifestSize: 1024, declaredLength: null);

        Assert.False(result.Ok);
        Assert.Contains("Downloaded update size did not match the update manifest.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChunkedResponseLongerThanManifest_IsRejected()
    {
        var result = await DownloadAsync(Payload(2048), manifestSize: 1024, declaredLength: null);

        Assert.False(result.Ok);
        Assert.Contains("Downloaded update size did not match the update manifest.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UntrustedDownloadHost_IsRejected()
    {
        var payload = Payload(64);
        var result = await DownloadAsync(payload, manifestSize: 64, declaredLength: 64,
            downloadUrl: "https://attacker.invalid/RedfurSync.exe");

        Assert.False(result.Ok);
        Assert.Contains("trusted HTTPS download", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashMismatch_IsRejected()
    {
        var payload = Payload(64);
        var wrongHash = Convert.ToHexString(SHA256.HashData(Payload(65))).ToLowerInvariant();
        var result = await DownloadAsync(payload, manifestSize: 64, declaredLength: 64, sha256: wrongHash);

        Assert.False(result.Ok);
        Assert.Contains("hash did not match", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonestSizeAndHash_Succeeds()
    {
        var payload = Payload(1024);
        var result = await DownloadAsync(payload, manifestSize: 1024, declaredLength: 1024);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1f, result.Progress);
        Assert.Equal(payload, result.WrittenBytes);
    }

    private static byte[] Payload(int length)
    {
        var buffer = new byte[length];
        for (var i = 0; i < length; i++) buffer[i] = (byte)(i % 251);
        return buffer;
    }

    private static async Task<DownloadResult> DownloadAsync(
        byte[] body,
        long manifestSize,
        long? declaredLength,
        string downloadUrl = DownloadUrl,
        string? sha256 = null)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = Path.Combine(temporaryDirectory.Path, "RedfurSync.update");

        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (declaredLength.HasValue)
            {
                response.Content = new ByteArrayContent(body);
                response.Content.Headers.ContentLength = declaredLength.Value;
            }
            else
            {
                // No Content-Length => chunked; only the streamed-byte check can catch a lie.
                response.Content = new StreamContent(new MemoryStream(body));
                response.Content.Headers.ContentLength = null;
            }
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return Task.FromResult(response);
        });

        // ApiKey/DeviceToken left empty so no telemetry request is issued on success.
        var config = new AppConfig { UpdateUrl = ManifestUrl };
        using var uploader = new UploadService(config, FakeHttpMessageHandler.Returning(HttpStatusCode.OK), handler);

        var job = new UploadJob
        {
            IsUpdate = true,
            FileName = "RedfurSync.exe",
            FilePath = destination,
            DownloadUrl = downloadUrl,
            FileSizeBytes = manifestSize,
            UpdateSha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant()
        };

        var ok = await uploader.DownloadUpdateAsync(job);
        var written = File.Exists(destination)
            ? await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken)
            : null;
        return new DownloadResult(ok, job.ErrorMessage ?? string.Empty, job.Progress, written);
    }

    private sealed record DownloadResult(bool Ok, string Error, float Progress, byte[]? WrittenBytes);
}
