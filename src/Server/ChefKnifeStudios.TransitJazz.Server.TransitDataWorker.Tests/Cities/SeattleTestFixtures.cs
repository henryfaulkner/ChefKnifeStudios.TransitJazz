using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ProtoBuf;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Cities;

internal static class SeattleTestFixtures
{
    public const string SoundTransitKeySentinel = "sentinel-seattle-sound-transit-key";
    public static readonly DateTimeOffset ReceiptUtc = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public sealed record StaticRoute(string RouteId, string? ShortName, int RouteType = 3,
        double Latitude = 47.60, double Longitude = -122.33);

    public static FeedMessage Feed(params FeedEntity[] entities) => new() { Entities = [.. entities] };

    public static FeedEntity Vehicle(string? descriptorId, string routeId, double latitude = 47.60,
        double longitude = -122.33, DateTimeOffset? timestamp = null, bool deleted = false, bool omitPosition = false)
    {
        var entity = new FeedEntity
        {
            Id = descriptorId ?? "missing-descriptor",
            IsDeleted = deleted,
            Vehicle = new VehiclePosition
            {
                Vehicle = new VehicleDescriptor { Id = descriptorId! },
                Trip = new TripDescriptor { RouteId = routeId },
                Position = omitPosition ? null! : new Position { Latitude = (float)latitude, Longitude = (float)longitude },
                Timestamp = timestamp is null ? null : (ulong)timestamp.Value.ToUnixTimeSeconds(),
            },
        };
        return entity;
    }

    public static byte[] ProtobufBytes(FeedMessage feed)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, feed);
        return stream.ToArray();
    }

    public static byte[] StaticZip(params StaticRoute[] routes)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "routes.txt", "route_id,route_short_name,route_type\n" + string.Join("\n",
                routes.Select(route => $"{route.RouteId},{route.ShortName ?? ""},{route.RouteType}")) + "\n");
            Write(archive, "trips.txt", "route_id,service_id,trip_id,shape_id\n" + string.Join("\n",
                routes.Select((route, index) => $"{route.RouteId},weekday,trip-{index},shape-{index}")) + "\n");
            Write(archive, "shapes.txt", "shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence\n" + string.Join("\n",
                routes.SelectMany((route, index) => new[]
                {
                    $"shape-{index},{route.Latitude:F6},{route.Longitude:F6},1",
                    $"shape-{index},{route.Latitude + 0.001:F6},{route.Longitude + 0.001:F6},2",
                })) + "\n");
        }
        return stream.ToArray();
    }

    public static HttpResponseMessage ProtobufResponse(FeedMessage feed) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(ProtobufBytes(feed)),
    };

    public static HttpResponseMessage ZipResponse(params StaticRoute[] routes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(StaticZip(routes)),
    };

    static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? UserAgent);

    public sealed class CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        readonly ConcurrentQueue<CapturedRequest> _requests = new();
        public IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Enqueue(new CapturedRequest(request.Method, request.RequestUri,
                request.Headers.UserAgent.ToString()));
            return await respond(request, cancellationToken);
        }
    }

    public sealed class CapturingHttpClientFactory(Func<string, HttpMessageHandler> handlerFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handlerFactory(name), disposeHandler: false);
    }

    public sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));

        sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
