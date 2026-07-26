using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using System.Text;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// P1-U1/P1-U2 (051 US2): IRouteShapeResponseCache is a plain singleton — ETag is a deterministic
// content hash (no clock in the contract, per reliability.md); repopulation swaps the entry
// atomically so readers holding an old reference always see intact, consistent bytes.
public class RouteShapeResponseCacheTests
{
    static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void SameBytes_ProduceSameETag()
    {
        var cache = new RouteShapeResponseCache();
        cache.Populate(RouteShapeResponseCacheKey.AllShapes("marta"), Bytes("{\"a\":1}"));
        var first = cache.Get(RouteShapeResponseCacheKey.AllShapes("marta"));

        cache.Populate(RouteShapeResponseCacheKey.AllRoutes("wmata"), Bytes("{\"a\":1}"));
        var second = cache.Get(RouteShapeResponseCacheKey.AllRoutes("wmata"));

        Assert.Equal(first!.Value.ETag, second!.Value.ETag);
    }

    [Fact]
    public void DifferentBytes_ProduceDifferentETag()
    {
        var cache = new RouteShapeResponseCache();
        cache.Populate(RouteShapeResponseCacheKey.AllShapes("marta"), Bytes("{\"a\":1}"));
        cache.Populate(RouteShapeResponseCacheKey.AllRoutes("marta"), Bytes("{\"a\":2}"));

        var a = cache.Get(RouteShapeResponseCacheKey.AllShapes("marta"));
        var b = cache.Get(RouteShapeResponseCacheKey.AllRoutes("marta"));

        Assert.NotEqual(a!.Value.ETag, b!.Value.ETag);
    }

    [Fact]
    public void Repopulate_SwapsEntry_OldEntryUnchanged()
    {
        var cache = new RouteShapeResponseCache();
        var key = RouteShapeResponseCacheKey.AllShapes("marta");
        cache.Populate(key, Bytes("{\"v\":1}"));
        var oldEntry = cache.Get(key)!.Value;

        cache.Populate(key, Bytes("{\"v\":2}"));
        var newEntry = cache.Get(key)!.Value;

        // The reference captured before repopulation is untouched (immutable entry).
        Assert.Equal(Bytes("{\"v\":1}"), oldEntry.Utf8Json);
        Assert.Equal(Bytes("{\"v\":2}"), newEntry.Utf8Json);
        Assert.NotEqual(oldEntry.ETag, newEntry.ETag);
    }

    [Fact]
    public void Get_UnpopulatedKey_ReturnsNull()
    {
        var cache = new RouteShapeResponseCache();

        var result = cache.Get(RouteShapeResponseCacheKey.AllShapes("never-loaded"));

        Assert.Null(result);
    }
}
