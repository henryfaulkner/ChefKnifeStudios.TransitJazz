using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public class RouteIdNormalizerTests
{
    [Theory]
    [InlineData("bx3", new[] { "uppercase" }, "BX3")]
    [InlineData("M15+", new[] { "plusToSbs" }, "M15-SBS")]
    [InlineData("Q06", new[] { "stripLeadingZeros" }, "Q6")]
    [InlineData("BX07", new[] { "stripLeadingZeros" }, "BX7")]
    [InlineData("Q06", new[] { "uppercase", "plusToSbs", "stripLeadingZeros" }, "Q6")]
    [InlineData("m15+", new[] { "uppercase", "plusToSbs", "stripLeadingZeros" }, "M15-SBS")]
    [InlineData("bx3", new[] { "uppercase", "plusToSbs", "stripLeadingZeros" }, "BX3")]
    [InlineData("S", new[] { "uppercase", "plusToSbs", "stripLeadingZeros" }, "S")]
    [InlineData("Q6", new[] { "stripLeadingZeros" }, "Q6")]
    [InlineData("anything", new[] { "bogusStep" }, "anything")]
    [InlineData("M15+", new string[0], "M15+")]
    [InlineData("Q006", new[] { "stripLeadingZeros" }, "Q6")]
    public void Apply_MatchesAcceptVector(string routeId, string[] steps, string expected)
    {
        var actual = RouteIdNormalizer.Apply(routeId, steps);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Apply_EmptySteps_IsIdentity()
    {
        Assert.Equal("M15+", RouteIdNormalizer.Apply("M15+", []));
    }

    [Fact]
    public void Apply_NeverThrows_ForUnknownStep()
    {
        var ex = Record.Exception(() => RouteIdNormalizer.Apply("anything", ["bogusStep"]));
        Assert.Null(ex);
    }

    [Fact]
    public void Apply_IsIdempotent_OnAlreadyNormalizedIds()
    {
        string[] fullPipeline = ["uppercase", "plusToSbs", "stripLeadingZeros"];
        var once = RouteIdNormalizer.Apply("m15+", fullPipeline);
        var twice = RouteIdNormalizer.Apply(once, fullPipeline);
        Assert.Equal(once, twice);
    }
}
