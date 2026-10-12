using System.Text.Json.Serialization;

namespace ChefKnifeStudios.TransitJazz.Shared.Stats;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StatsOverlayRequest
{
    public string? Scope { get; init; }
    public string? CitySlug { get; init; }
    public string[]? RouteJoinKeys { get; init; }
}
