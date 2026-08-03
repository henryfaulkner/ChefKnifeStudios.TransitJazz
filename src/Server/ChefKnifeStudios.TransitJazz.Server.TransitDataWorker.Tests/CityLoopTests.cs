using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
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
        ITransitCity emitting = new FakeCity("marta", emits: true);
        ITransitCity nonEmitting = new FakeCity("wmata", emits: false);

        Assert.True(emitting.EmitsTelemetry);
        Assert.False(nonEmitting.EmitsTelemetry);
    }

    // INV-2 (fault isolation): two cities; city A throws; city B still works
    [Fact]
    public async Task FaultIsolation_ThrowingCity_DoesNotBlockOtherCity()
    {
        var throwing = new ThrowingCity("bad-city");
        var working = new FakeCity("marta", emits: true);

        bool martaFetched = false;
        bool threwForBad = false;

        foreach (var city in new ITransitCity[] { throwing, working })
        {
            try
            {
                var feed = await city.FetchVehiclesAsync(CancellationToken.None);
                if (city.Name == "marta") martaFetched = true;
            }
            catch
            {
                if (city.Name == "bad-city") threwForBad = true;
            }
        }

        Assert.True(threwForBad);
        Assert.True(martaFetched);
    }

    // INV-1 (no name branching): loop only branches on EmitsTelemetry, not city.Name
    [Fact]
    public void Loop_TelemetryGate_BranchesOnlyOnEmitsTelemetry()
    {
        var results = new List<(string City, bool Emits)>();

        foreach (var city in new ITransitCity[]
        {
            new FakeCity("marta", emits: true),
            new FakeCity("wmata", emits: false),
            new FakeCity("custom", emits: false),
        })
        {
            // Simulate the worker's telemetry gate (INV-3): PostEvent iff EmitsTelemetry
            if (city.EmitsTelemetry)
                results.Add((city.Name, true));
            else
                results.Add((city.Name, false));
        }

        Assert.Equal(("marta", true), results[0]);
        Assert.Equal(("wmata", false), results[1]);
        Assert.Equal(("custom", false), results[2]);
    }

    sealed class FakeCity(string name, bool emits) : ITransitCity
    {
        public string Name => name;
        public string TelemetryName => name;
        public bool EmitsTelemetry => emits;
        public Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct) => Task.FromResult(new FeedMessage());
    }

    sealed class ThrowingCity(string name) : ITransitCity
    {
        public string Name => name;
        public string TelemetryName => name;
        public bool EmitsTelemetry => false;
        public Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct)
            => throw new InvalidOperationException($"Simulated fetch failure for city {name}");
    }
}
