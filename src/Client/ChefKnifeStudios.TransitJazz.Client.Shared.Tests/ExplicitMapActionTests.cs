using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.ClientExperience;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class ExplicitMapActionTests
{
    [Fact]
    public async Task ExplicitFailedDisplayChangeMayUseGlobalOutcomeOnlyWhenRestoredAndNoLocalExplanation()
    {
        var settings = new TestSettingsService();
        var controller = new SettingsChangeController(settings);
        var outcome = await controller.ChangeAsync(
            nameof(Settings.AreCheckpointsVisible),
            false,
            (_, _) => Task.FromResult(SettingsEffectResult.Failure));

        Assert.Equal(SettingsChangeStatus.ApplyFailedRestored, outcome.Status);
        Assert.Equal(true, outcome.ConfirmedValue);
        Assert.Equal(
            NotificationIssueCode.DisplaySettingFailed,
            SettingsChangeController.GetEligibleDisplaySettingFailureCode(
                nameof(Settings.AreCheckpointsVisible), outcome, explicitlyRequested: true, hasLocalFeedback: false));
    }

    [Fact]
    public async Task HoverBackgroundPersistenceAndLocalStatusStayOutOfM6()
    {
        var settings = new TestSettingsService();
        var controller = new SettingsChangeController(settings);
        var failed = await controller.ChangeAsync(
            nameof(Settings.IsBusesVisible),
            false,
            (_, _) => Task.FromResult(SettingsEffectResult.Failure));
        var saveFailureController = new SettingsChangeController(settings, (_, _) => Task.FromException(new InvalidOperationException()));
        var appliedOnly = await saveFailureController.ChangeAsync(
            nameof(Settings.IsBusesVisible), false,
            (_, _) => Task.FromResult(SettingsEffectResult.Success));

        Assert.Null(SettingsChangeController.GetEligibleDisplaySettingFailureCode(
            nameof(Settings.IsBusesVisible), failed, explicitlyRequested: false, hasLocalFeedback: false));
        Assert.Null(SettingsChangeController.GetEligibleDisplaySettingFailureCode(
            nameof(Settings.IsBusesVisible), failed, explicitlyRequested: true, hasLocalFeedback: true));
        Assert.Null(SettingsChangeController.GetEligibleDisplaySettingFailureCode(
            nameof(Settings.IsBusesVisible), appliedOnly, explicitlyRequested: true, hasLocalFeedback: false));
    }

    [Fact]
    public async Task FailedExplicitCityNavigationRetainsOldPageAndActionStatus()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock, conditionalDeliveryMappingsEnabled: true);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var experience = new RecordingAttempts();
        var cityActivationId = Guid.NewGuid();
        var navigation = new StubNavigationManager("https://app.example.test/#atlanta");
        var controller = new CityNavigationController(
            new FakeJsRuntime(new InvalidOperationException("interop failed")), navigation, coordinator, experience);

        var outcome = await controller.NavigateToCityAsync("boston", new NotificationScope(1, "atlanta"), cityActivationId);

        Assert.False(outcome.NavigationStarted);
        Assert.True(outcome.OldPageUsable);
        Assert.Equal("boston", outcome.FailedCity);
        Assert.Equal("boston", controller.FailedCity);
        var started = Assert.Single(experience.Started);
        Assert.Equal("atlanta", started.CitySlug);
        Assert.Equal("city_navigation", started.Journey);
        Assert.Equal("city_navigation", started.Action);
        Assert.Equal(cityActivationId, started.CityActivationId);
        Assert.Equal(started.Id, Assert.Single(experience.Finished).Id);
        Assert.Equal("failed", experience.Finished[0].Outcome);
        Assert.Equal("owner_failed", experience.Finished[0].Reason);
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        Assert.Equal(
            [NotificationIssueCode.CityNavigationFailed],
            Assert.Single(presenter.Added).IssueCodes);
    }

    [Fact]
    public async Task SuccessfulExplicitNavigationAndNormalHashResolutionStaySilent()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock, conditionalDeliveryMappingsEnabled: true);
        var navigation = new StubNavigationManager("https://app.example.test/#unknown-city");
        var controller = new CityNavigationController(new FakeJsRuntime(), navigation, coordinator);

        var outcome = await controller.NavigateToCityAsync("boston", new NotificationScope(1, "unknown-city"));
        clock.Advance(TimeSpan.FromSeconds(5));
        await coordinator.EvaluateAsync();

        Assert.True(outcome.NavigationStarted);
        Assert.Null(controller.FailedCity);
        Assert.Equal("unknown-city", navigation.ResolveCity());
        Assert.Equal(0, coordinator.ActiveIncidentCount);
    }

    sealed class TestSettingsService : ISettingsService
    {
        readonly Settings _settings = new();
        public Settings GetSettings() => _settings;
        public void SaveSettings(Settings settings) { }
        public T? GetSettingValue<T>(string propertyName) =>
            typeof(Settings).GetProperty(propertyName)?.GetValue(_settings) is T value ? value : default;
        public void SetSettingValue<T>(string propertyName, T value) =>
            typeof(Settings).GetProperty(propertyName)?.SetValue(_settings, value);
    }

    sealed class FakeJsRuntime(Exception? failure = null) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (failure is not null) throw failure;
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    sealed class RecordingAttempts : IClientExperienceAttemptCapture
    {
        public List<StartedAttempt> Started { get; } = [];
        public List<FinishedAttempt> Finished { get; } = [];
        public Guid StartAttempt(string citySlug, string journey, long deadlineForegroundMs, Guid? cityActivationId = null,
            Guid? parentActionId = null, string? action = null)
        {
            var id = Guid.NewGuid();
            Started.Add(new(id, citySlug, journey, cityActivationId, action));
            return id;
        }
        public void FinishAttempt(Guid attemptId, string citySlug, string journey, string outcome, string reason, Guid? cityActivationId = null)
            => Finished.Add(new(attemptId, outcome, reason));
        public sealed record StartedAttempt(Guid Id, string CitySlug, string Journey, Guid? CityActivationId, string? Action);
        public sealed record FinishedAttempt(Guid Id, string Outcome, string Reason);
    }

    sealed class StubNavigationManager : NavigationManager
    {
        public StubNavigationManager() : this("https://app.example.test/#atlanta") { }
        public StubNavigationManager(string uri) => Initialize(uri, uri);
    }

    sealed class RecordingPresenter : INotificationPresenter
    {
        public List<NotificationSummary> Added { get; } = [];
        public ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken)
        {
            Added.Add(summary);
            return ValueTask.FromResult(true);
        }
        public ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
