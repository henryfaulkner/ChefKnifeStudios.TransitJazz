using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChefKnifeStudios.MartaJazz.Shared;

public static class JsonOptions
{
    public static JsonSerializerOptions Get()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            // RouteShapeProperties.Mode is a TransitMode enum serialized as a string
            // ("Bus"/"Rail") in the stored GeoJSON; this round-trips it back to the enum.
            Converters = { new JsonStringEnumConverter() }
        };
    }
}
