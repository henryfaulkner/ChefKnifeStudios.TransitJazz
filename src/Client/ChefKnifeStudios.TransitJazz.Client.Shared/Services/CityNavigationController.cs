using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

public sealed record CityNavigationOutcome(long OperationGeneration, string? FailedCity, bool NavigationStarted, bool OldPageUsable);

public sealed class CityNavigationController(
    IJSRuntime jsRuntime,
    NavigationManager navigationManager,
    NotificationCoordinator coordinator)
{
    long _operationGeneration;
    public string? FailedCity { get; private set; }

    public async Task<CityNavigationOutcome> NavigateToCityAsync(string city, NotificationScope scope)
    {
        if (!IsConfiguredCity(city))
            return new(Volatile.Read(ref _operationGeneration), null, false, true);

        var generation = Interlocked.Increment(ref _operationGeneration);
        var originalUri = navigationManager.Uri;
        try
        {
            var encodedCity = JsonSerializer.Serialize(city);
            await jsRuntime.InvokeVoidAsync("eval", $"location.hash={encodedCity};location.reload()");
            if (generation != Volatile.Read(ref _operationGeneration))
                return new(generation, null, false, true);

            FailedCity = null;
            return new(generation, null, true, false);
        }
        catch
        {
            if (generation != Volatile.Read(ref _operationGeneration))
                return new(generation, null, false, true);

            if (!string.Equals(originalUri, navigationManager.Uri, StringComparison.Ordinal))
            {
                FailedCity = null;
                return new(generation, null, true, false);
            }

            FailedCity = city;
            coordinator.Report(new NotificationIssue(
                NotificationIssueCode.CityNavigationFailed,
                scope,
                generation));
            return new(generation, city, false, true);
        }
    }

    static bool IsConfiguredCity(string city) => city is
        CityNames.Marta or CityNames.Wmata or CityNames.Mbta or CityNames.Nymta
        or CityNames.Ttc or CityNames.Septa or CityNames.Rtd or CityNames.Seattle;
}
