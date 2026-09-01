using System.Net;

namespace RedfurSync.Tests;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;
    private readonly List<RequestSnapshot> _requests = [];

    public FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
    }

    public IReadOnlyList<RequestSnapshot> Requests
    {
        get
        {
            lock (_requests) return _requests.ToArray();
        }
    }

    public bool IsDisposed { get; private set; }

    public static FakeHttpMessageHandler Returning(HttpStatusCode statusCode) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));

    public static (FakeHttpMessageHandler Handler, PendingRequest Pending) Blocking()
    {
        var pending = new PendingRequest();
        return (new FakeHttpMessageHandler(pending.SendAsync), pending);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var snapshot = new RequestSnapshot(
            request.Method,
            request.RequestUri,
            request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray()),
            request.Content?.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray()),
            request.Content?.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult());
        lock (_requests) _requests.Add(snapshot);
        return _sendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) IsDisposed = true;
        base.Dispose(disposing);
    }
}

internal sealed record RequestSnapshot(
    HttpMethod Method,
    Uri? Uri,
    IReadOnlyDictionary<string, string[]> Headers,
    IReadOnlyDictionary<string, string[]>? ContentHeaders,
    byte[]? Body);

internal sealed class PendingRequest
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<HttpResponseMessage> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public void Complete(HttpResponseMessage response) => _completion.TrySetResult(response);

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _started.TrySetResult();
        return _completion.Task.WaitAsync(cancellationToken);
    }
}