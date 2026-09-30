using System.Net;
using System.Threading.Channels;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

public sealed class FileWatcherServiceTests
{
    [Fact]
    public async Task SourceChangesWhileSnapshotUploads_QueuesLatestContents()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var sourcePath = temporaryDirectory.WriteFile("source/PriceTableNA.lua", "snapshot-a");
        var spoolPath = temporaryDirectory.CreateDirectory("spool");
        var requests = new SequencedHttpHandler();
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = "fixture-key",
            DisplayName = "Fixture"
        };
        using var uploader = new UploadService(config, requests, FakeHttpMessageHandler.Returning(HttpStatusCode.OK));
        using var watcher = new FileWatcherService(_ => { }, config, uploader, spoolPath, _ => { });

        await watcher.EnqueueFileForTestAsync(sourcePath);
        var first = await requests.NextAsync();
        File.WriteAllText(sourcePath, "snapshot-b");
        await watcher.EnqueueFileForTestAsync(sourcePath);

        Assert.Equal(1, requests.Count);
        first.Complete(new HttpResponseMessage(HttpStatusCode.OK));

        var second = await requests.NextAsync();
        Assert.Contains("snapshot-a", first.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot-b", first.BodyText, StringComparison.Ordinal);
        Assert.Contains("snapshot-b", second.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot-a", second.BodyText, StringComparison.Ordinal);
        second.Complete(new HttpResponseMessage(HttpStatusCode.OK));

        await WaitForJobsAsync(watcher, jobs => jobs.Count == 2 && jobs.All(job => job.Status == UploadStatus.Done));
        Assert.Empty(Directory.EnumerateFiles(spoolPath));
    }

    [Fact]
    public void IsGsFile_CorrectlyIdentifiesGuildStoreFiles()
    {
        Assert.True(FileWatcherService.IsGsFile("GS00Data.lua"));
        Assert.True(FileWatcherService.IsGsFile("GS01Data.lua"));
        Assert.True(FileWatcherService.IsGsFile("GS17Data.lua"));
        Assert.False(FileWatcherService.IsGsFile("FissalRelay.lua"));
        Assert.False(FileWatcherService.IsGsFile("PriceTableNA.lua"));
        Assert.False(FileWatcherService.IsGsFile("ItemLookUpTable_EN.lua"));
        Assert.False(FileWatcherService.IsGsFile("RaffleGold.lua"));
        Assert.False(FileWatcherService.IsGsFile("GSNotLua.txt"));
        Assert.False(FileWatcherService.IsGsFile(null));
        Assert.False(FileWatcherService.IsGsFile(string.Empty));
    }

    [Fact]
    public void JobOrdering_PrioritizesErrorsThenUploadingThenQueuedThenDone_AndNonGsBeforeGs()
    {
        int GetPriority(UploadStatus s) => s switch
        {
            UploadStatus.Failed => 0,
            UploadStatus.Uploading => 1,
            UploadStatus.Queued or UploadStatus.Cancelled => 2,
            UploadStatus.Done => 3,
            _ => 4
        };
        bool IsGs(string f) => FileWatcherService.IsGsFile(f);

        var jobs = new List<UploadJob>
        {
            new() { FileName = "GS02Data.lua", Status = UploadStatus.Done },
            new() { FileName = "FissalRelay.lua", Status = UploadStatus.Done },
            new() { FileName = "PriceTableNA.lua", Status = UploadStatus.Uploading },
            new() { FileName = "GS01Data.lua", Status = UploadStatus.Uploading },
            new() { FileName = "GS05Data.lua", Status = UploadStatus.Failed },
            new() { FileName = "FissalRelay.lua", Status = UploadStatus.Failed },
            new() { FileName = "RaffleGold.lua", Status = UploadStatus.Queued },
            new() { FileName = "GS03Data.lua", Status = UploadStatus.Queued },
        };

        var sorted = jobs
            .OrderBy(j => GetPriority(j.Status))
            .ThenBy(j => IsGs(j.FileName) ? 1 : 0)
            .ThenBy(j => j.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 1. Errors first (non-GS then GS)
        Assert.Equal("FissalRelay.lua", sorted[0].FileName);
        Assert.Equal(UploadStatus.Failed, sorted[0].Status);

        Assert.Equal("GS05Data.lua", sorted[1].FileName);
        Assert.Equal(UploadStatus.Failed, sorted[1].Status);

        // 2. Uploading next (non-GS then GS)
        Assert.Equal("PriceTableNA.lua", sorted[2].FileName);
        Assert.Equal(UploadStatus.Uploading, sorted[2].Status);

        Assert.Equal("GS01Data.lua", sorted[3].FileName);
        Assert.Equal(UploadStatus.Uploading, sorted[3].Status);

        // 3. Queued next (non-GS then GS)
        Assert.Equal("RaffleGold.lua", sorted[4].FileName);
        Assert.Equal(UploadStatus.Queued, sorted[4].Status);

        Assert.Equal("GS03Data.lua", sorted[5].FileName);
        Assert.Equal(UploadStatus.Queued, sorted[5].Status);

        // 4. Done last (non-GS then GS)
        Assert.Equal("FissalRelay.lua", sorted[6].FileName);
        Assert.Equal(UploadStatus.Done, sorted[6].Status);

        Assert.Equal("GS02Data.lua", sorted[7].FileName);
        Assert.Equal(UploadStatus.Done, sorted[7].Status);
    }

    [Fact]
    public async Task TriggerInitialSweep_WhenFissalRelayPresent_IgnoresGsFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var savedVars = temporaryDirectory.CreateDirectory("SavedVariables");
        File.WriteAllText(Path.Combine(savedVars, "FissalRelay.lua"), "-- Fissal data");
        File.WriteAllText(Path.Combine(savedVars, "GS00Data.lua"), "-- MM data");

        var spoolPath = temporaryDirectory.CreateDirectory("spool");
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = "fixture-key",
            DisplayName = "Fixture",
            SyncMasterMerchantFiles = false,
        };
        using var uploader = new UploadService(config, FakeHttpMessageHandler.Returning(HttpStatusCode.OK), FakeHttpMessageHandler.Returning(HttpStatusCode.OK));
        using var watcher = new FileWatcherService(_ => { }, config, uploader, spoolPath, _ => { })
        {
            WatchRootProvider = () => temporaryDirectory.Path,
        };

        await watcher.ReconcileExistingFilesAsync();

        Assert.Contains(watcher.Jobs, j => j.FileName == "FissalRelay.lua");
        Assert.DoesNotContain(watcher.Jobs, j => j.FileName == "GS00Data.lua");
        await WaitForJobsAsync(watcher, jobs => jobs.Count == 1 && jobs.All(j => j.Status == UploadStatus.Done));
    }

    [Fact]
    public async Task TriggerInitialSweep_WhenFissalRelayAbsent_EnqueuesGsFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var savedVars = temporaryDirectory.CreateDirectory("SavedVariables");
        File.WriteAllText(Path.Combine(savedVars, "GS00Data.lua"), "-- MM data");

        var spoolPath = temporaryDirectory.CreateDirectory("spool");
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = "fixture-key",
            DisplayName = "Fixture",
            SyncMasterMerchantFiles = false,
        };
        using var uploader = new UploadService(config, FakeHttpMessageHandler.Returning(HttpStatusCode.OK), FakeHttpMessageHandler.Returning(HttpStatusCode.OK));
        using var watcher = new FileWatcherService(_ => { }, config, uploader, spoolPath, _ => { })
        {
            WatchRootProvider = () => temporaryDirectory.Path,
        };

        await watcher.ReconcileExistingFilesAsync();

        Assert.Contains(watcher.Jobs, j => j.FileName == "GS00Data.lua");
        await WaitForJobsAsync(watcher, jobs => jobs.Count == 1 && jobs.All(j => j.Status == UploadStatus.Done));
    }

    [Fact]
    public async Task TriggerInitialSweep_WhenSyncMasterMerchantFilesTrue_EnqueuesBoth()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var savedVars = temporaryDirectory.CreateDirectory("SavedVariables");
        File.WriteAllText(Path.Combine(savedVars, "FissalRelay.lua"), "-- Fissal data");
        File.WriteAllText(Path.Combine(savedVars, "GS00Data.lua"), "-- MM data");

        var spoolPath = temporaryDirectory.CreateDirectory("spool");
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = "fixture-key",
            DisplayName = "Fixture",
            SyncMasterMerchantFiles = true,
        };
        using var uploader = new UploadService(config, FakeHttpMessageHandler.Returning(HttpStatusCode.OK), FakeHttpMessageHandler.Returning(HttpStatusCode.OK));
        using var watcher = new FileWatcherService(_ => { }, config, uploader, spoolPath, _ => { })
        {
            WatchRootProvider = () => temporaryDirectory.Path,
        };

        await watcher.ReconcileExistingFilesAsync();

        Assert.Contains(watcher.Jobs, j => j.FileName == "FissalRelay.lua");
        Assert.Contains(watcher.Jobs, j => j.FileName == "GS00Data.lua");
        await WaitForJobsAsync(watcher, jobs => jobs.Count == 2 && jobs.All(j => j.Status == UploadStatus.Done));
    }

    private static async Task WaitForJobsAsync(FileWatcherService watcher, Func<IReadOnlyList<UploadJob>, bool> predicate)
    {
        if (predicate(watcher.Jobs.ToArray())) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (!predicate(watcher.Jobs.ToArray())) return;
            completion.TrySetResult();
        }
        watcher.JobsChanged += OnChanged;
        try
        {
            OnChanged();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            watcher.JobsChanged -= OnChanged;
        }
    }

    private sealed class SequencedHttpHandler : HttpMessageHandler
    {
        private readonly Channel<SequencedRequest> _requests = Channel.CreateUnbounded<SequencedRequest>();
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ValueTask<SequencedRequest> NextAsync() => _requests.Reader.ReadAsync();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var pending = new SequencedRequest(
                System.Text.Encoding.UTF8.GetString(await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
            Interlocked.Increment(ref _count);
            await _requests.Writer.WriteAsync(pending, cancellationToken);
            return await pending.Response.WaitAsync(cancellationToken);
        }
    }

    private sealed class SequencedRequest(string bodyText)
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string BodyText { get; } = bodyText;
        public Task<HttpResponseMessage> Response => _response.Task;
        public void Complete(HttpResponseMessage response) => _response.TrySetResult(response);
    }
}