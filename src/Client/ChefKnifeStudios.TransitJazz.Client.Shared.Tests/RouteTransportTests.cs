using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class RouteTransportTests
{
    [Fact]
    public async Task CallerCancellationDuringGetPropagatesInsteadOfBecomingAnErrorResult()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var http = CreateHttpService(handler);
        using var cancellation = new CancellationTokenSource();

        var request = http.GetAsync<string>("https://api.example.test/routes", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task CallerCancellationDuringBodyReadPropagates()
    {
        var content = new HeldReadContent();
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }));
        var http = CreateHttpService(handler);
        using var cancellation = new CancellationTokenSource();

        var request = http.GetAsync<string>("https://api.example.test/routes", cancellation.Token);
        await content.ReadStarted.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task IndependentTransportTimeoutMapsToSafeBoundedError()
    {
        var handler = new StubHandler((_, _) => Task.FromException<HttpResponseMessage>(
            new TaskCanceledException("transport timeout sentinel")));
        var http = CreateHttpService(handler);

        var result = await http.GetAsync<string>("https://api.example.test/routes");

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("transport timeout sentinel", string.Join(";", result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedResponseBodyIsNotReturnedAndResponseContentIsDisposed()
    {
        var content = new TrackingContent("private response body sentinel");
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "upstream detail sentinel",
            Content = content
        }));
        var http = CreateHttpService(handler);

        var result = await http.GetAsync<string>("https://api.example.test/routes");

        var errors = string.Join(";", result.Errors);
        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("private response body sentinel", errors, StringComparison.Ordinal);
        Assert.DoesNotContain("upstream detail sentinel", errors, StringComparison.Ordinal);
        Assert.Equal(1, content.DisposeCount);
    }

    static HttpService CreateHttpService(HttpMessageHandler handler)
        => new(new HttpClient(handler), NullLogger<HttpService>.Instance);

    sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    sealed class TrackingContent(string text) : HttpContent
    {
        public int DisposeCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Encoding.UTF8.GetByteCount(text);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    sealed class HeldReadContent : HttpContent
    {
        readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => HoldReadAsync(CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => HoldReadAsync(cancellationToken);

        async Task HoldReadAsync(CancellationToken cancellationToken)
        {
            _readStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
