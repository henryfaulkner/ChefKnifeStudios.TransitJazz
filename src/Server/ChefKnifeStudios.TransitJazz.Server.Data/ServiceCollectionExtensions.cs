using ChefKnifeStudios.TransitJazz.Server.Data.Repos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChefKnifeStudios.TransitJazz.Server.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection RegisterDataServices(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(optionsBuilder =>
        {
            optionsBuilder.UseNpgsql(connectionString);
        });

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));

        return services;
    }
}

