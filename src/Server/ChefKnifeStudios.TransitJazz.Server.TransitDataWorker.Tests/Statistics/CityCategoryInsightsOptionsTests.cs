using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class CityCategoryInsightsOptionsTests
{
    [Fact]
    public void Disabled_capture_allows_worker_cycles_slower_than_unused_default_gap()
    {
        var options = new CityCategoryInsightsOptions();

        options.Validate(cycleIntervalSeconds: 60);

        options.Enabled = true;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate(cycleIntervalSeconds: 60));
    }

    [Fact]
    public void Enabled_time_zones_require_a_named_Iana_identifier()
    {
        CityCategoryInsightsOptions.ValidateIanaTimeZoneId("America/New_York");

        Assert.Throws<ArgumentException>(() => CityCategoryInsightsOptions.ValidateIanaTimeZoneId("Eastern Standard Time"));
        Assert.Throws<TimeZoneNotFoundException>(() => CityCategoryInsightsOptions.ValidateIanaTimeZoneId("America/Not_A_Real_Zone"));
    }
}
