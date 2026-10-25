using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationPolicyTests
{
    [Theory]
    [InlineData(NotificationIssueCode.AudioResumeFailed)]
    [InlineData(NotificationIssueCode.RequiredVoicesPartiallyUnavailable)]
    [InlineData(NotificationIssueCode.RequiredVoicesUnavailable)]
    [InlineData(NotificationIssueCode.AudioTextureChangeFailed)]
    [InlineData(NotificationIssueCode.AudioFilterFailed)]
    [InlineData(NotificationIssueCode.AudioStopFailed)]
    [InlineData(NotificationIssueCode.SettingsNotSaved)]
    [InlineData(NotificationIssueCode.SettingsApplyFailed)]
    public void ConditionalAudioAndSettingsMappingsStayClosedUntilExplicitEnablement(NotificationIssueCode code)
    {
        var issue = new NotificationIssue(code, IsVerifiedAudioStillPlaying: true);
        var disabled = new NotificationPolicy(new NotificationTestTimeProvider());
        var enabled = new NotificationPolicy(new NotificationTestTimeProvider(), conditionalAudioSettingsMappingsEnabled: true);

        Assert.False(disabled.Report(issue));
        Assert.True(enabled.Report(issue));
    }

    [Fact]
    public void EligibleFailuresKeepFirstBurstDeadline()
    {
        var clock = new NotificationTestTimeProvider();
        var policy = new NotificationPolicy(clock);

        policy.Report(new NotificationIssue(NotificationIssueCode.LiveConnectionFailed));
        clock.Advance(TimeSpan.FromMilliseconds(400));
        policy.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromMilliseconds(800));
        policy.Report(new NotificationIssue(NotificationIssueCode.MapUnavailable));

        clock.Advance(TimeSpan.FromMilliseconds(799));
        Assert.Null(policy.TryCreateSummary());

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var summary = Assert.IsType<NotificationSummary>(policy.TryCreateSummary());
        Assert.Equal(NotificationSummaryKind.General, summary.Kind);
        Assert.Empty(summary.Capabilities);
        Assert.Equal(NotificationSeverity.Error, summary.Severity);
    }

    [Fact]
    public void SameCapabilityUsesOneCapabilitySpecificSummary()
    {
        var (clock, policy) = CreatePolicy();
        policy.Report(new NotificationIssue(NotificationIssueCode.LiveConnectionFailed));
        policy.Report(new NotificationIssue(NotificationIssueCode.LiveJoinFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        var summary = Assert.IsType<NotificationSummary>(policy.TryCreateSummary());
        Assert.Equal(NotificationSummaryKind.SingleCapability, summary.Kind);
        Assert.Equal([NotificationCapability.Live], summary.Capabilities);
        Assert.Equal("ToastLiveFailure", summary.MessageKey);
    }

    [Fact]
    public void TwoCapabilitiesUseBoundedCapabilitySummary()
    {
        var (clock, policy) = CreatePolicy();
        policy.Report(new NotificationIssue(NotificationIssueCode.LiveConnectionFailed));
        policy.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        var summary = Assert.IsType<NotificationSummary>(policy.TryCreateSummary());
        Assert.Equal(NotificationSummaryKind.MultipleCapabilities, summary.Kind);
        Assert.Equal(2, summary.Capabilities.Count);
        Assert.Equal([NotificationCapability.Live, NotificationCapability.Routes], summary.Capabilities);
    }

    [Fact]
    public void HighestSeverityWinsAcrossCapabilities()
    {
        var (clock, policy) = CreatePolicy();
        policy.Report(new NotificationIssue(NotificationIssueCode.LiveInterrupted));
        policy.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        var summary = Assert.IsType<NotificationSummary>(policy.TryCreateSummary());
        Assert.Equal(NotificationSeverity.Error, summary.Severity);
    }

    [Fact]
    public void LocallyOwnedImpactDoesNotInflateGlobalSummary()
    {
        var (clock, policy) = CreatePolicy();
        policy.Report(new NotificationIssue(NotificationIssueCode.AudioUnavailable, IsLocallyOwned: true));
        policy.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        var summary = Assert.IsType<NotificationSummary>(policy.TryCreateSummary());
        Assert.Equal([NotificationCapability.Routes], summary.Capabilities);
        Assert.DoesNotContain(NotificationCapability.Audio, summary.Capabilities);
    }

    [Fact]
    public void RawDiagnosticTextCannotBecomePolicyCopy()
    {
        const string secretSentinel = "sentinel-password-do-not-render";
        var (clock, policy) = CreatePolicy();
        policy.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        var summary = Assert.IsType<NotificationSummary>(policy.TryCreateSummary());
        var stringProperties = typeof(NotificationIssue).GetProperties()
            .Where(property => property.PropertyType == typeof(string));

        Assert.Empty(stringProperties);
        Assert.DoesNotContain(secretSentinel, summary.MessageKey, StringComparison.Ordinal);
        Assert.StartsWith("Toast", summary.MessageKey, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineQualificationRequiresObservedEligibleNetworkFailure()
    {
        var (clock, policy) = CreatePolicy();
        policy.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        var unqualified = Assert.IsType<NotificationSummary>(policy.TryCreateSummary(browserOffline: true));
        Assert.False(unqualified.IsOfflineQualified);

        var (qualifiedClock, qualifiedPolicy) = CreatePolicy();
        qualifiedPolicy.Report(new NotificationIssue(
            NotificationIssueCode.RouteLoadFailed,
            OperationGeneration: 2,
            IsObservedNetworkFailure: true));
        qualifiedClock.Advance(TimeSpan.FromSeconds(2));

        var qualified = Assert.IsType<NotificationSummary>(qualifiedPolicy.TryCreateSummary(browserOffline: true));
        Assert.True(qualified.IsOfflineQualified);
        Assert.Equal(unqualified.Severity, qualified.Severity);
    }

    [Fact]
    public void ChangingOwnerGenerationsDoNotGrowIncidentStorage()
    {
        var (_, policy) = CreatePolicy();

        for (var generation = 1; generation <= 10_000; generation++)
        {
            policy.Report(new NotificationIssue(
                NotificationIssueCode.RouteLoadFailed,
                OperationGeneration: generation));
        }

        Assert.Equal(1, policy.ActiveIncidentCount);
    }

    static (NotificationTestTimeProvider Clock, NotificationPolicy Policy) CreatePolicy()
    {
        var clock = new NotificationTestTimeProvider();
        return (clock, new NotificationPolicy(clock));
    }
}
