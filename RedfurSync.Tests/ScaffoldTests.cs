using System.Net;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void TestProject_IsDiscoverable()
    {
        Assert.True(true);
    }

    [Fact]
    public void UploadService_AcceptsOwnedHttpHandlers()
    {
        var syncHandler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var updateHandler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);

        using (new UploadService(new AppConfig(), syncHandler, updateHandler))
        {
        }

        Assert.True(syncHandler.IsDisposed);
        Assert.True(updateHandler.IsDisposed);
    }

    [Fact]
    public void TemporaryDirectory_ContainsFilesAndCleansUp()
    {
        string root;
        using (var temporaryDirectory = new TemporaryDirectory())
        {
            root = temporaryDirectory.Path;
            var file = temporaryDirectory.WriteFile("nested/fixture.txt", "fixture");
            Assert.True(File.Exists(file));
            Assert.Throws<ArgumentException>(() => temporaryDirectory.WriteFile("../outside.txt", "no"));
        }

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task FakeHandler_CapturesRequestAndPropagatesCancellation()
    {
        var (handler, pending) = FakeHttpMessageHandler.Blocking();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var send = client.PostAsync("https://relay.invalid/test", new StringContent("fixture"), cancellation.Token);

        await pending.Started;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("fixture", System.Text.Encoding.UTF8.GetString(request.Body!));
    }
}