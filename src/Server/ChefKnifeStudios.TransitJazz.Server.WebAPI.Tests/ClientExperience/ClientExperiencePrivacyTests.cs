using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperiencePrivacyTests
{
    static readonly string[] ForbiddenMemberNames =
    [
        "settingValue", "latitude", "longitude", "coordinates", "routeId", "routeJoinKey", "vehicleId",
        "url", "token", "exception", "exceptionMessage", "renderedCopy", "userAgent", "ipAddress"
    ];

    [Fact]
    public void VersionOneEventContractHasNoSensitiveValueFields()
    {
        var memberNames = typeof(ClientExperienceEvent).GetProperties()
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(memberNames.Intersect(ForbiddenMemberNames, StringComparer.Ordinal));
    }

    [Fact]
    public void UnknownSensitiveEventMembersAreRejectedByStrictJsonDeserialization()
    {
        const string payload = """
            {"schemaVersion":1,"measurementVersion":1,"eventKind":"attempt_started","eventId":"19536e4e-f06b-47b7-8e90-0e7e000aab1f","pageSessionId":"8544e767-2d3e-41c5-8cd8-04ec6b774e1b","sequence":1,"occurredAtUtc":"2026-11-08T00:00:00Z","pageElapsedMs":0,"citySlug":"atlanta","clientRelease":"test","settingValue":"visitor-secret"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientExperienceEvent>(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
