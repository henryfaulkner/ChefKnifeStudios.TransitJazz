using System.Collections.Frozen;
using System.Net;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>Source-side validation for values that may enter a structured event.</summary>
public static class StructuredLogRedactor
{
    static readonly FrozenSet<string> AllowedProperties = new[]
    {
        "EventName", "EventVersion", "EventId", "CycleId", "Outcome", "ReasonCode", "City",
        "DurationMs", "DeploymentRevision", "ExceptionType", "TonesEmitted", "VehiclesProcessed",
        "FeedFreshnessSeconds", "CrossingsEmitted", "CrossingsSuppressedFirstSeen",
        "CrossingsSuppressedDeltaLeq0", "CrossingsSuppressedTeleport", "CrossingsSuppressedTransfer",
        "BatchWireBytes", "PublishAttempted", "PublishSucceeded",
    }.ToFrozenSet(StringComparer.Ordinal);

    static readonly string[] SecretMarkers =
    [
        "access_token=", "api_key=", "apikey=", "authorization:", "bearer ", "cookie:",
        "connectionstring=", "defaultendpointsprotocol=", "sharedaccesssignature=", "sig=",
    ];

    public static IReadOnlySet<string> AllowedEventProperties => AllowedProperties;

    public static StructuredLogEvent Validate(StructuredLogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        foreach (var property in logEvent.ToLogState())
        {
            if (property.Key == "{OriginalFormat}") continue;
            if (!AllowedProperties.Contains(property.Key))
                throw new InvalidOperationException($"Property '{property.Key}' is not allowed in a structured event.");
            if (property.Value is string value) RejectSecretBearingValue(property.Key, value);
        }
        return logEvent;
    }

    public static string SafeEndpointIdentity(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return "unknown-endpoint";
        if (endpoint.Contains('\r') || endpoint.Contains('\n')) return "invalid-endpoint";
        if (LooksSecretBearing(endpoint)) return "redacted-endpoint";

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            var host = uri.IdnHost;
            if (string.IsNullOrWhiteSpace(host)) return "unknown-endpoint";
            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"{uri.Scheme.ToLowerInvariant()}://{host.ToLowerInvariant()}{port}";
        }

        // A caller may pass an already bounded source label (for example, MartaBusFeed).
        return IsSafeLabel(endpoint) ? endpoint : "invalid-endpoint";
    }

    public static string SafeExceptionType(Exception exception) =>
        exception is null ? "UnknownException" : SafeTypeName(exception.GetType().Name);

    public static string SafeHttpStatus(HttpStatusCode statusCode) => ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static void ValidatePropertyName(string propertyName)
    {
        if (!AllowedProperties.Contains(propertyName))
            throw new ArgumentException($"Property '{propertyName}' is not in the structured event allow-list.", nameof(propertyName));
    }

    public static void RejectSecretBearingValue(string propertyName, string? value)
    {
        ValidatePropertyName(propertyName);
        if (value is not null && LooksSecretBearing(value))
            throw new ArgumentException($"Property '{propertyName}' contains a prohibited secret-bearing value.", nameof(value));
    }

    static bool LooksSecretBearing(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return SecretMarkers.Any(normalized.Contains)
            || normalized.StartsWith("eyj", StringComparison.Ordinal) // common JWT prefix; never log bearer material
            || normalized.Contains("-----begin ", StringComparison.Ordinal)
            || normalized.Contains("client_secret", StringComparison.Ordinal);
    }

    static bool IsSafeLabel(string value) => value.Length <= 80
        && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':');

    static string SafeTypeName(string typeName) => IsSafeLabel(typeName) ? typeName : "UnknownException";
}
