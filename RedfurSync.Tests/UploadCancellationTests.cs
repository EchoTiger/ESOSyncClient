using System.Net;
using System.Threading.Channels;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

/// A job parked on the upload throttle must observe cancellation instead of
/// claiming a freed slot once an in-flight upload completes.
[Collection("Serial")]
public sealed class UploadCancellationTests
{
    [Fact]
    public async Task CancelQueuedJob_WhileAllUploadSlotsBusy_EndsCancelledWithoutUploading()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var spoolPath = temporaryDirectory.CreateDirectory("spool");
        var requests = new SequencedUploadHandler();
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = "fixture-key",
            DisplayName = "Fixture"
        };
        using var uploader = new UploadService(config, requests, FakeHttpMessageHandler.Returning(HttpStatusCode.OK));
        using var watcher = new FileWatcherService(_ => { }, config, uploader, spoolPath, _ => { });

        // Non-sales file names, so no /mm/known pre-flight desynchronises the sequence.
        var sources = new[] { "PriceTableNA.lua", "ItemLookUpTable_EN.lua", "RaffleGold.lua", "PriceDataNA.lua" }
            .Select((name, index) => temporaryDirectory.WriteFile($"source/{name}", $"payload-{index}"))
            .ToArray();

        foreach (var source in sources)
            await watcher.EnqueueFileForTestAsync(source);

        // Occupy all three throttle slots; the fourth job is parked on WaitAsync.
        var inFlight = new List<SequencedRequest>();
        for (var i = 0; i < 3; i++) inFlight.Add(await requests.NextAsync());
        Assert.Equal(3, requests.Count);

        var parked = watcher.Jobs.Single(job => job.SourcePath == sources[3]);
        watcher.CancelJob(parked);

        foreach (var request in inFlight)
            request.Complete(new HttpResponseMessage(HttpStatusCode.OK));

        await WaitForJobsAsync(watcher, jobs =>
            jobs.Count == 4 && jobs.All(job => job.Status is UploadStatus.Done or UploadStatus.Cancelled or UploadStatus.Failed));

        Assert.Equal(UploadStatus.Cancelled, parked.Status);
        // The freed slots must not resurrect the cancelled job.
        Assert.Equal(3, requests.Count);

        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(UploadStatus.Cancelled, parked.Status);
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task CancelQueuedJob_WhileAllUploadSlotsBusy_LeavesNoUnobservedTaskException()
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            lock (unobserved) unobserved.Add(args.Exception);
            args.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            await CancelQueuedJob_WhileAllUploadSlotsBusy_EndsCancelledWithoutUploading();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            lock (unobserved) Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    private static async Task WaitForJobsAsync(FileWatcherService watcher, Func<IReadOnlyList<UploadJob>, bool> predicate)
    {
        if (predicate(watcher.Jobs.ToArray())) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (predicate(watcher.Jobs.ToArray())) completion.TrySetResult();
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

    private sealed class SequencedUploadHandler : HttpMessageHandler
    {
        private readonly Channel<SequencedRequest> _requests = Channel.CreateUnbounded<SequencedRequest>();
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public async Task<SequencedRequest> NextAsync() =>
            await _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            var pending = new SequencedRequest();
            await _requests.Writer.WriteAsync(pending, cancellationToken);
            return await pending.Response.WaitAsync(cancellationToken);
        }
    }

    private sealed class SequencedRequest
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HttpResponseMessage> Response => _response.Task;

        public void Complete(HttpResponseMessage response) => _response.TrySetResult(response);
    }
}
