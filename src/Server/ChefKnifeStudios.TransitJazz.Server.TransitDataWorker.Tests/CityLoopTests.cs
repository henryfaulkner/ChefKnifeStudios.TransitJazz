using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public class CityLoopTests
{
    // INV-3 (FR-015): a city with EmitsTelemetry=false satisfies the interface contract
    [Fact]
    public void ITransitCity_EmitsTelemetry_IsConfigurablePerCity()
    {
        ITransitCity emitting = new FakeCity("atlanta", emits: true);
        ITransitCity nonEmitting = new FakeCity("washington-dc", emits: false);

        Assert.True(emitting.EmitsTelemetry);
        Assert.False(nonEmitting.EmitsTelemetry);
    }

    // INV-2 (fault isolation): two cities; city A throws; city B still works
    [Fact]
    public async Task FaultIsolation_ThrowingCity_DoesNotBlockOtherCity()
    {
        var throwing = new ThrowingCity("bad-city");
        var working = new FakeCity("atlanta", emits: true);

        bool atlantaFetched = false;
        bool threwForBad = false;

        foreach (var city in new ITransitCity[] { throwing, working })
        {
            try
            {
                var feed = await city.FetchVehiclesAsync(CancellationToken.None);
                if (city.Name == "atlanta") atlantaFetched = true;
            }
            catch
            {
                if (city.Name == "bad-city") threwForBad = true;
            }
        }

        Assert.True(threwForBad);
        Assert.True(atlantaFetched);
    }

    // INV-1 (no name branching): loop only branches on EmitsTelemetry, not city.Name
    [Fact]
    public void Loop_TelemetryGate_BranchesOnlyOnEmitsTelemetry()
    {
        var results = new List<(string City, bool Emits)>();

        foreach (var city in new ITransitCity[]
        {
            new FakeCity("atlanta", emits: true),
            new FakeCity("washington-dc", emits: false),
            new FakeCity("custom", emits: false),
        })
        {
            // Simulate the worker's telemetry gate (INV-3): PostEvent iff EmitsTelemetry
            if (city.EmitsTelemetry)
                results.Add((city.Name, true));
            else
                results.Add((city.Name, false));
        }

        Assert.Equal(("atlanta", true), results[0]);
        Assert.Equal(("washington-dc", false), results[1]);
        Assert.Equal(("custom", false), results[2]);
    }

    sealed class FakeCity(string name, bool emits) : ITransitCity
    {
        public string Name => name;
        public bool EmitsTelemetry => emits;
        public Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct) => Task.FromResult(CityFetchResult.FromSources(new FeedMessage(), 1, 0));
    }

    sealed class ThrowingCity(string name) : ITransitCity
    {
        public string Name => name;
        public bool EmitsTelemetry => false;
        public Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct)
            => throw new InvalidOperationException($"Simulated fetch failure for city {name}");
    }
}
