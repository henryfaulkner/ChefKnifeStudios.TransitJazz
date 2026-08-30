using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class StructuredLoggingRedactionTests
{
    [Theory]
    [InlineData("https://feeds.example.test/vehicles?api_key=secret")]
    [InlineData("Authorization: Bearer secret")]
    [InlineData("DefaultEndpointsProtocol=https;AccountKey=secret")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    public void SecretBearingEndpoint_IsNeverReturned(string input)
    {
        var safe = StructuredLogRedactor.SafeEndpointIdentity(input);
        Assert.DoesNotContain("secret", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", safe, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(input, safe);
    }

    [Fact]
    public void SafeEndpointIdentity_RetainsOnlySchemeHostAndPort()
    {
        Assert.Equal("https://feeds.example.test:8443",
            StructuredLogRedactor.SafeEndpointIdentity("https://feeds.example.test:8443/feed?key=not-logged"));
    }

    [Fact]
    public void ExceptionFormatting_UsesTypeOnly()
    {
        var exception = new InvalidOperationException("bearer secret and request body");

        Assert.Equal("InvalidOperationException", StructuredLogRedactor.SafeExceptionType(exception));
    }

    [Fact]
    public void EventValidation_RejectsFreeFormExceptionTextAndSecretValues()
    {
        var exceptionText = StructuredLogEvent.Create(
            StructuredLogEventName.CityInputFailed,
            StructuredLogOutcome.Failed,
            "event-1", "cycle-1", "atlanta", StructuredLogReasonCode.INPUT_FAILED,
            new StructuredLogDiagnosticContext { ExceptionType = "token=secret" });

        Assert.Throws<ArgumentException>(() => exceptionText.Validate());
        Assert.Throws<ArgumentException>(() => StructuredLogRedactor.RejectSecretBearingValue(
            "DeploymentRevision", "Authorization: Bearer secret"));
    }

    [Fact]
    public void EventState_ContainsOnlyContractProperties()
    {
        var eventData = StructuredLogEvent.Create(
            StructuredLogEventName.WorkerStarted,
            StructuredLogOutcome.Succeeded,
            StructuredLogEvent.NewEventId());

        var state = StructuredLogRedactor.Validate(eventData).ToLogState();
        Assert.All(state.Where(x => x.Key != "{OriginalFormat}"), x =>
            Assert.Contains(x.Key, StructuredLogRedactor.AllowedEventProperties));
    }
}

