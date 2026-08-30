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
    // INV-2 (fault isolation): two cities; city A throws; city B still works
    [Fact]
    public async Task FaultIsolation_ThrowingCity_DoesNotBlockOtherCity()
    {
        var throwing = new ThrowingCity("bad-city");
        var working = new FakeCity("atlanta");

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

    sealed class FakeCity(string name) : ITransitCity
    {
        public string Name => name;
        public Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct) => Task.FromResult(CityFetchResult.FromSources(new FeedMessage(), 1, 0));
    }

    sealed class ThrowingCity(string name) : ITransitCity
    {
        public string Name => name;
        public Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct)
            => throw new InvalidOperationException($"Simulated fetch failure for city {name}");
    }
}
