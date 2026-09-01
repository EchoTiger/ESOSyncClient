using System.Net;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

/// Guards the P1 guarantee: calling StartAsync repeatedly must not grow the watcher set
/// and must not schedule more than one update check. The discriminator is the observed
/// update-manifest HTTP request count and the update-job count — never a bare
/// "_updateTimer is a single field" check, which passes against any implementation.
public sealed class StartAsyncIdempotencyTests
{
    private const string ManifestPath = "/update/manifest";

    [Fact]
    public async Task RepeatedStartAsync_LeavesOneWatcherSetAndOneUpdateCheck()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var spoolPath = temporaryDirectory.CreateDirectory("spool");
        var liveRoot = temporaryDirectory.CreateDirectory("live");
        temporaryDirectory.CreateDirectory("live/SavedVariables");
        temporaryDirectory.CreateDirectory("live/AddOns/TamrielTradeCentre");
        temporaryDirectory.CreateDirectory("live/AddOns/LibEsoHubPrices");

        var updateHandler = new ManifestCountingHandler(ManifestPath);
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = "fixture-key",
            UpdateUrl = $"https://relay.invalid{ManifestPath}",
        };
        using var uploader = new UploadService(config, FakeHttpMessageHandler.Returning(HttpStatusCode.OK), updateHandler);
        using var watcher = new FileWatcherService(_ => { }, config, uploader, spoolPath, _ => { })
        {
            WatchRootProvider = () => liveRoot,
            StartupUpdateDelay = TimeSpan.FromMilliseconds(150),
            UpdateInterval = TimeSpan.FromHours(1),   // never fires during the test
        };

        // Spaced so a duplicate schedule would be observable as an extra manifest request.
        for (var i = 0; i < 3; i++)
        {
            await watcher.StartAsync();
            await Task.Delay(350, TestContext.Current.CancellationToken);
        }

        // One watcher set, stable across repeated calls.
        Assert.Equal(3, watcher.ActiveWatcherCount);

        // Only the first StartAsync armed the update machinery; ensure it actually ran.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (updateHandler.ManifestRequests == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);

        Assert.Equal(1, updateHandler.ManifestRequests);                 // discriminator
        Assert.Single(watcher.Jobs, job => job.IsUpdate);                // one update job
    }

    private sealed class ManifestCountingHandler : HttpMessageHandler
    {
        private readonly string _manifestPath;
        private int _manifestRequests;

        public ManifestCountingHandler(string manifestPath) => _manifestPath = manifestPath;

        public int ManifestRequests => Volatile.Read(ref _manifestRequests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == _manifestPath)
            {
                Interlocked.Increment(ref _manifestRequests);
                var manifest = System.Text.Json.JsonSerializer.Serialize(new
                {
                    version = "9.0.0",
                    downloadUrl = "https://relay.invalid/update/download",
                    changelog = "test",
                    sizeBytes = 1024,
                    sha256 = new string('a', 64)
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(manifest, System.Text.Encoding.UTF8, "application/json")
                };
            }

            // Download endpoint: report a Content-Length that contradicts the manifest (512 != 1024)
            // so DownloadUpdateAsync fails before it writes any file — no tmp pollution.
            var download = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[512])
            };
            download.Content.Headers.ContentLength = 512;
            return download;
        }
    }
}
