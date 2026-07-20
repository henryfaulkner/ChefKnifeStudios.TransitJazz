using System.ComponentModel;
using ChefKnifeStudios.MartaJazz.Client.Core.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Tests;

/// <summary>
/// A stub <see cref="IApplicationViewModel"/> (util-testing/test-doubles.md: "stub —
/// returns hard-coded values") that serves a controlled <see cref="RouteShapes"/>
/// catalog to <c>RouteFilterViewModel</c> so its pure derivations (CategoryOrder,
/// active counts) can be unit-tested with no SignalR, no HTTP, no browser.
///
/// The <see cref="NotificationReceived"/> event is exposed so a test can push a batch
/// through the same path the live stream uses.
/// </summary>
public sealed class FakeApplicationViewModel : IApplicationViewModel
{
    readonly Dictionary<string, RouteShapeFeature> _shapes;

    public FakeApplicationViewModel(IEnumerable<RouteShapeFeature> shapes)
    {
        _shapes = shapes.ToDictionary(s => s.Properties.JoinKey, StringComparer.Ordinal);
        RoutesLoaded = true;
    }

    public event SignalRNotificationHandler? NotificationReceived;
    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyDictionary<string, RouteShapeFeature> RouteShapes => _shapes;
    public bool IsConnected => true;
    public bool RoutesLoaded { get; }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishBatch(List<EventEnvelope> batch) => RaiseAsync(batch);

    /// <summary>Test seam: fan a batch through NotificationReceived like the live hub.</summary>
    public Task RaiseAsync(List<EventEnvelope> batch) =>
        NotificationReceived?.Invoke(batch) ?? Task.CompletedTask;

    // ── Test-data builder ────────────────────────────────────────────────────
    // Post-044 RouteShapeProperties carries `string Category` + `int RouteType`
    // (data-model.md). This helper uses those members, so it — and every test
    // through it — is RED until T004 adds them to the shared record.
    public static RouteShapeFeature Shape(string joinKey, string category, int routeType) =>
        new(
            "Feature",
            new RouteShapeGeometry("LineString", [[-84.39, 33.75], [-84.38, 33.76]]),
            new RouteShapeProperties(
                RouteId: joinKey,
                RouteShortName: joinKey,
                Color: "#CC0000",
                TextColor: "#FFFFFF",
                Category: category,
                RouteType: routeType,
                City: "testcity"));
}
