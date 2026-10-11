using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryStatisticsCaptureHostRegistrationTests
{
    [Fact]
    public void Enabled_capture_runtime_resolves_the_time_provider_and_hosted_sweeper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICategoryStatisticsSink>(NullCategoryStatisticsSink.Instance);
        services.AddCategoryStatisticsCaptureRuntime(new CityCategoryInsightsOptions
        {
            Enabled = true,
        });

        using var provider = services.BuildServiceProvider();
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.NotNull(provider.GetRequiredService<CityCategoryStatisticsCapture>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is CityCategoryStatisticsCaptureLifecycleService);
    }
}
