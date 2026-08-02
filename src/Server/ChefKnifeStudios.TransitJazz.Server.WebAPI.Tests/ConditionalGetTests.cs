using ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// P1-U3 (051 US2): the If-None-Match vs ETag decision is a pure helper so the full boundary
// matrix lives at unit level; TestHost (P1-S1/S2) proves only the wiring once.
public class ConditionalGetTests
{
    [Theory]
    [InlineData("\"abc123\"", "\"abc123\"", true)]                    // exact match -> 304
    [InlineData(null, "\"abc123\"", false)]                            // absent header -> 200
    [InlineData("\"stale\"", "\"abc123\"", false)]                     // stale ETag -> 200
    [InlineData("\"other\", \"abc123\"", "\"abc123\"", true)]          // multi-value with match -> 304
    [InlineData("*", "\"abc123\"", true)]                              // wildcard -> 304
    public void IsNotModified_MatchesContract(string? ifNoneMatch, string etag, bool expectedNotModified)
    {
        var header = ifNoneMatch is null ? StringValues.Empty : new StringValues(ifNoneMatch);

        var result = HttpCaching.IsNotModified(header, etag);

        Assert.Equal(expectedNotModified, result);
    }
}
