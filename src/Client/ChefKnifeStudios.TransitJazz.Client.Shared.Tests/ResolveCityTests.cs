using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Shared;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public class ResolveCityTests
{
    static NavigationManager NavFor(string uri)
    {
        var nav = new StubNavigationManager();
        nav.Initialize(uri);
        return nav;
    }

    [Theory]
    [InlineData("https://app.example.com/#atlanta", "atlanta")]
    [InlineData("https://app.example.com/#washington-dc", "washington-dc")]
    public void ResolveCity_ReturnsFragment_Lowercased(string uri, string expected)
    {
        Assert.Equal(expected, NavFor(uri).ResolveCity());
    }

    [Theory]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/#")]
    [InlineData("https://app.example.com/#   ")]
    public void ResolveCity_ReturnsDefaultSlug_ForEmptyOrWhitespaceFragment(string uri)
    {
        Assert.Equal(CityNames.Marta, NavFor(uri).ResolveCity());
    }

    [Theory]
    [InlineData("https://app.example.com/#ATLANTA")]
    [InlineData("https://app.example.com/#Atlanta")]
    public void ResolveCity_IsCaseInsensitive(string uri)
    {
        Assert.Equal("atlanta", NavFor(uri).ResolveCity());
    }

    sealed class StubNavigationManager : NavigationManager
    {
        public void Initialize(string uri) => Initialize(uri, uri);
    }
}
