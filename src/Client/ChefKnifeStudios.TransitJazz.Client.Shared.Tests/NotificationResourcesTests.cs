using System.Globalization;
using System.Resources;
using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationResourcesTests
{
    static readonly ResourceManager Resources = new(
        "ChefKnifeStudios.TransitJazz.Client.Shared.Resources.RouteFilterResources",
        typeof(RouteFilterResources).Assembly);

    [Fact]
    public void EveryIssueHasLocalizedSeverityImpactActionAndNativeCloseLabel()
    {
        AssertLocalized(RouteFilterResources.ToastSeverityWarning);
        AssertLocalized(RouteFilterResources.ToastSeverityError);
        AssertLocalized(RouteFilterResources.ToastClose);

        foreach (var code in Enum.GetValues<NotificationIssueCode>())
        {
            AssertLocalized(NotificationPolicy.GetImpactResourceKey(code));
            AssertLocalized(NotificationPolicy.GetActionResourceKey(code));
            Assert.NotNull(NotificationPolicy.GetCapability(code));
            Assert.NotNull(NotificationPolicy.GetSeverity(code));
        }
    }

    [Fact]
    public void NotificationSummaryCarriesOnlyFixedCopyKeysAndNoDiagnosticText()
    {
        var clock = new NotificationTestTimeProvider();
        var policy = new NotificationPolicy(clock);
        policy.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        var summary = Assert.IsType<NotificationSummary>(policy.TryCreateSummary());

        Assert.Equal("ToastSeverityError", summary.SeverityKey);
        Assert.Equal("ToastRoutesFailure", summary.ImpactKey);
        Assert.Equal("ToastActionReload", summary.ActionKey);
        Assert.Equal("ToastClose", summary.CloseKey);
        Assert.DoesNotContain(typeof(NotificationIssue).GetProperties(), property => property.PropertyType == typeof(string));
    }

    static void AssertLocalized(string key)
    {
        var value = Resources.GetString(key, CultureInfo.InvariantCulture);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Missing resource: {key}");
        Assert.NotEqual(key, value);
    }
}
