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