using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Client.Core.ClientExperience;
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
    NotificationCoordinator coordinator,
    IClientExperienceAttemptCapture? experienceAttempts = null)
{
    long _operationGeneration;
    public string? FailedCity { get; private set; }

    public async Task<CityNavigationOutcome> NavigateToCityAsync(string city, NotificationScope scope, Guid? cityActivationId = null)
    {
        if (!IsConfiguredCity(city))
            return new(Volatile.Read(ref _operationGeneration), null, false, true);

        var generation = Interlocked.Increment(ref _operationGeneration);
        var actionId = Guid.NewGuid();
        var attemptId = SafeStart(experienceAttempts, scope.CitySlug, actionId, cityActivationId);
        var originalUri = navigationManager.Uri;
        try
        {
            var encodedCity = JsonSerializer.Serialize(city);
            await jsRuntime.InvokeVoidAsync("eval", $"location.hash={encodedCity};location.reload()");
            if (generation != Volatile.Read(ref _operationGeneration))
            {
                SafeFinish(experienceAttempts, attemptId, scope.CitySlug, "superseded", "superseded");
                return new(generation, null, false, true);
            }

            FailedCity = null;
            SafeFinish(experienceAttempts, attemptId, scope.CitySlug, "succeeded", "owner_confirmed");
            return new(generation, null, true, false);
        }
        catch
        {
            if (generation != Volatile.Read(ref _operationGeneration))
            {
                SafeFinish(experienceAttempts, attemptId, scope.CitySlug, "superseded", "superseded");
                return new(generation, null, false, true);
            }

            if (!string.Equals(originalUri, navigationManager.Uri, StringComparison.Ordinal))
            {
                FailedCity = null;
                SafeFinish(experienceAttempts, attemptId, scope.CitySlug, "succeeded", "owner_confirmed");
                return new(generation, null, true, false);
            }

            FailedCity = city;
            SafeFinish(experienceAttempts, attemptId, scope.CitySlug, "failed", "owner_failed");
            coordinator.Report(new NotificationIssue(
                NotificationIssueCode.CityNavigationFailed,
                scope,
                generation));
            return new(generation, city, false, true);
        }
    }

    static Guid SafeStart(IClientExperienceAttemptCapture? capture, string citySlug, Guid actionId, Guid? cityActivationId)
    {
        try { return capture?.StartAttempt(citySlug, "city_navigation", 30_000, cityActivationId, actionId, "city_navigation") ?? Guid.Empty; }
        catch { return Guid.Empty; }
    }

    static void SafeFinish(IClientExperienceAttemptCapture? capture, Guid attemptId, string citySlug, string outcome, string reason)
    {
        if (attemptId == Guid.Empty) return;
        try { capture?.FinishAttempt(attemptId, citySlug, "city_navigation", outcome, reason); }
        catch { }
    }

    static bool IsConfiguredCity(string city) => city is
        CityNames.Marta or CityNames.Wmata or CityNames.Mbta or CityNames.Nymta
        or CityNames.Ttc or CityNames.Septa or CityNames.Rtd or CityNames.Seattle;
}
