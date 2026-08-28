using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public class MetricsOptionsTests
{
    [Fact]
    public void DisabledDefaults_AreValid() => new MetricsOptions().Validate(10, isProduction: true);

    [Fact]
    public void ProductionListener_IsRejected() => Assert.Throws<OptionsValidationException>(() =>
        new MetricsOptions { LocalPrometheusEnabled = true }.Validate(10, isProduction: true));

    [Fact]
    public void CloudEndpoint_MustBeHttpsMetricsPath() => Assert.Throws<OptionsValidationException>(() =>
        new MetricsOptions { Enabled = true, OtlpMetricsEndpoint = "http://example.test", OtlpAuthorization = "Basic x" }.Validate(10, false));
}
