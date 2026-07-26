using System.Text.Json;
using System.Text.Json.Serialization;
using ChefKnifeStudios.TransitJazz.Shared.Events;

namespace ChefKnifeStudios.TransitJazz.Shared;

public static class JsonSettings
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(),
            new EventEnvelopeConverter()
        }
    };

    public static void ApplyTo(JsonSerializerOptions target)
    {
        target.PropertyNamingPolicy = DefaultOptions.PropertyNamingPolicy;
        target.DefaultIgnoreCondition = DefaultOptions.DefaultIgnoreCondition;
        target.NumberHandling = DefaultOptions.NumberHandling;
        target.WriteIndented = DefaultOptions.WriteIndented;
        foreach (var converter in DefaultOptions.Converters)
            target.Converters.Add(converter);
    }
}
