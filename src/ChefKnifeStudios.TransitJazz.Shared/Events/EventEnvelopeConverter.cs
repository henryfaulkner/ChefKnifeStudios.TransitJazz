using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed class EventEnvelopeConverter : JsonConverter<EventEnvelope>
{
    private static readonly Dictionary<string, Type> _eventTypes;

    static EventEnvelopeConverter()
    {
        _eventTypes = typeof(EventEnvelope).Assembly
            .GetTypes()
            .Where(t => typeof(ISignalREvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToDictionary(t => t.Name);
    }

    public override void Write(Utf8JsonWriter writer, EventEnvelope value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("eventType", value.EventType);
        writer.WriteString("timestamp", value.Timestamp);
        writer.WritePropertyName("payload");
        JsonSerializer.Serialize(writer, value.Payload, value.Payload.GetType(), options);
        writer.WriteEndObject();
    }

    public override EventEnvelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("eventType", out var eventTypeProp))
            throw new JsonException("Missing EventType property");

        var eventType = eventTypeProp.GetString()
            ?? throw new JsonException("Missing EventType property");

        if (!_eventTypes.TryGetValue(eventType, out var payloadType))
            throw new JsonException($"Unknown EventType: {eventType} (strict mode enabled)");

        var timestamp = root.TryGetProperty("timestamp", out var tsProp)
            ? tsProp.GetString() ?? string.Empty
            : string.Empty;

        var payloadElement = root.GetProperty("payload");
        var payload = JsonSerializer.Deserialize(payloadElement.GetRawText(), payloadType, options) as ISignalREvent
            ?? throw new JsonException($"Failed to deserialize payload as {payloadType.Name}");

        return new EventEnvelope(eventType, DateTimeOffset.Parse(timestamp), payload);
    }
}
