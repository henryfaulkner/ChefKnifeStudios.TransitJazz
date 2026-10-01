using ChefKnifeStudios.TransitJazz.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class DataRegistrationTests
{
    [Fact]
    public void RegistersScopedContextAndFactoryAgainstTransitJazzDatabase()
    {
        var services = new ServiceCollection();
        services.RegisterDataServices(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TransitJazzDB"] = "Host=localhost;Database=transitjazz"
            })
            .Build());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        Assert.NotNull(context);
        using var created = factory.CreateDbContext();
        Assert.IsType<AppDbContext>(created);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", created.Database.ProviderName);
    }

    [Fact]
    public void MissingConnectionStringFailsWithoutEchoingASecret()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().RegisterDataServices(new ConfigurationBuilder().Build()));

        Assert.Contains("TransitJazzDB", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
