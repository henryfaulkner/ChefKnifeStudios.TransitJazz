using ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperienceValidatorTests
{
    [Fact]
    public void AcceptsFixedAttemptStartAndMarksClockSkew()
    {
        var now = DateTimeOffset.UtcNow;
        var result = ClientExperienceValidator.Validate(Event("city_map_start") with { OccurredAtUtc = now.AddMinutes(-6) }, now);

        Assert.True(result.IsValid);
        Assert.True(result.ClockSkewed);
    }

    [Theory]
    [InlineData("future_journey", "invalid_journey")]
    [InlineData("city_map_start", "invalid_payload")]
    public void RejectsUnknownCodesAndPerKindFields(string journey, string expectedReason)
    {
        var value = Event("city_map_start") with
        {
            Journey = journey,
            Severity = journey == "city_map_start" ? "error" : null
        };
        var result = ClientExperienceValidator.Validate(value, DateTimeOffset.UtcNow);
        Assert.False(result.IsValid);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Fact]
    public void RejectsWindowPartitionsThatDoNotBalance()
    {
        var value = Event("city_map_start") with
        {
            EventKind = "observation_window",
            AttemptId = null,
            Journey = null,
            Eligible = null,
            DeadlineForegroundMs = null,
            WindowSequence = 1,
            WindowStartMs = 0,
            WindowEndMs = 60_000,
            ObservedMs = 50_000,
            GapMs = 0,
            ExcludedMs = 0,
            StateDurations = [new ClientExperienceStateDuration { Capability = "map", State = "healthy", DurationMs = 49_999 }]
        };

        var result = ClientExperienceValidator.Validate(value, DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_window_partition", result.Reason);
    }

    [Fact]
    public void EnabledIntakeRequiresConfiguredDatabase()
    {
        var options = new ClientExperienceOptions { Enabled = true };
        Assert.Throws<InvalidOperationException>(() => options.Validate(null));
    }

    [Fact]
    public void SyntheticIntakeRequiresAnEnabledServerAndBoundedRunnerSecret()
    {
        var valid = new ClientExperienceOptions
        {
            Enabled = true,
            SyntheticIntakeEnabled = true,
            SyntheticIntakeToken = new string('s', 32)
        };
        valid.Validate("Host=localhost;Database=client_experience_disposable");

        Assert.Throws<InvalidOperationException>(() => new ClientExperienceOptions
        {
            Enabled = true,
            SyntheticIntakeEnabled = true
        }.Validate("Host=localhost;Database=client_experience_disposable"));
        Assert.Throws<InvalidOperationException>(() => new ClientExperienceOptions
        {
            SyntheticIntakeEnabled = true,
            SyntheticIntakeToken = new string('s', 32)
        }.Validate("Host=localhost;Database=client_experience_disposable"));
    }

    static ClientExperienceEvent Event(string journey) => new()
    {
        SchemaVersion = 1,
        MeasurementVersion = 1,
        EventKind = "attempt_started",
        EventId = Guid.NewGuid(),
        PageSessionId = Guid.NewGuid(),
        CityActivationId = null,
        Sequence = 1,
        OccurredAtUtc = DateTimeOffset.UtcNow,
        PageElapsedMs = 1,
        CitySlug = "atlanta",
        ClientRelease = "test",
        AttemptId = Guid.NewGuid(),
        Journey = journey,
        Eligible = true,
        DeadlineForegroundMs = 60_000
    };
}
