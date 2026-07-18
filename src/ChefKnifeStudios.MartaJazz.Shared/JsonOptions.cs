using System.Text.Json;

namespace ChefKnifeStudios.MartaJazz.Shared;

public static class JsonOptions
{
    public static JsonSerializerOptions Get()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }
}
