using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Enums;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;
using System.Net;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsFeatureFlagTests
{
    [Fact]
    public void ProductionEntryPolicyIsOffByDefaultAndUsesTheSingleConfiguredFlag()
    {
        var defaultFlags = new FeatureFlagService();
        var enabledFlags = new FeatureFlagService(new Dictionary<FeatureFlags, bool>
        {
            [FeatureFlags.StatsOverlay] = true
        });
        var unrelatedFlags = new FeatureFlagService(new Dictionary<FeatureFlags, bool>
        {
            [FeatureFlags.StatsOverlay] = false
        });

        Assert.False(StatsOverlayFeaturePolicy.IsAvailable(defaultFlags));
        Assert.False(StatsOverlayFeaturePolicy.IsAvailable(unrelatedFlags));
        Assert.True(StatsOverlayFeaturePolicy.IsAvailable(enabledFlags));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task StoreStartsClosedPreparationWhenStatsAreEnabled(
        bool statsEnabled, int expectedInitialCalls)
    {
        var flags = Flags(statsEnabled);
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            flags);
        try
        {
            await store.StartAsync("toronto");
            if (expectedInitialCalls > 0)
            {
                var initial = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal("city", initial.Request.Scope);
                Assert.Equal(expectedInitialCalls, endpoints.Calls.Count);
                initial.Return(new HttpServiceResponse<StatsOverlayResponse>(
                    Result<StatsOverlayResponse>.Success(StatsSnapshotAcceptanceTests.ValidResponse(
                        new StatsSelection("city", "toronto", []), clock.GetUtcNow().UtcDateTime)),
                    HttpStatusCode.OK, null, null));
                var all = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal("all", all.Request.Scope);
                Assert.Equal(2, endpoints.Calls.Count);
            }
            else Assert.Empty(endpoints.Calls);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task OpeningDuringStartupJoinsPreparationWithoutAnotherFlag()
    {
        var flags = Flags(statsEnabled: true);
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(DateTimeOffset.UtcNow),
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            flags);
        StatsSnapshotStoreTests.StatsEndpointCall? call = null;
        Task? pending = null;
        try
        {
            await store.StartAsync("toronto");
            call = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            var selection = new StatsSelection("city", "toronto", []);
            pending = store.EnsureRequestedAsync(selection);
            Assert.Single(endpoints.Calls);
            Assert.Equal("city", call.Request.Scope);
            Assert.Equal("toronto", call.Request.CitySlug);
        }
        finally { await store.StopAsync(); }
        await pending!;
        Assert.True(call!.CancellationToken.IsCancellationRequested);
        Assert.True(call.Completed.Task.IsCompleted);
    }

    static FeatureFlagService Flags(bool statsEnabled) => new(new Dictionary<FeatureFlags, bool>
    {
        [FeatureFlags.StatsOverlay] = statsEnabled
    });
}
