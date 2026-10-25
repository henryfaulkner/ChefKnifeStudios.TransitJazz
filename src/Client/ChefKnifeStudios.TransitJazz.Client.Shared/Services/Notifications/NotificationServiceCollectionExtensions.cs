using System;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;

public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<NotificationCoordinator>(serviceProvider => new NotificationCoordinator(
            serviceProvider.GetRequiredService<TimeProvider>(),
            logger: serviceProvider.GetService<ILogger<NotificationCoordinator>>()));
        services.AddSingleton<CoreCapabilityNotificationOwner>();
        services.AddScoped<ToastService>();
        services.AddScoped<IToastService>(serviceProvider => serviceProvider.GetRequiredService<ToastService>());
        services.AddScoped<INotificationPresenter>(serviceProvider => serviceProvider.GetRequiredService<ToastService>());

        return services;
    }
}
