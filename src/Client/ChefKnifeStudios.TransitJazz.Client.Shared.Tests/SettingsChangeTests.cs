using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Client.Core.ClientExperience;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class SettingsChangeTests
{
    [Fact]
    public async Task ExplicitSettingsEffectAndPersistenceShareActionIdWithoutCapturingValue()
    {
        var settings = new TestSettingsService();
        var capture = new RecordingAttempts();
        var controller = new SettingsChangeController(settings, experienceAttempts: capture);

        var outcome = await controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), true,
            (_, _) => Task.FromResult(SettingsEffectResult.Success));

        Assert.Equal(SettingsChangeStatus.Applied, outcome.Status);
        Assert.Equal(["settings_effect", "settings_persistence"], capture.Started.Select(x => x.Journey));
        Assert.Equal(capture.Started[0].ParentActionId, capture.Started[1].ParentActionId);
        Assert.All(capture.Started, attempt => Assert.Equal("display_setting", attempt.Action));
        Assert.All(capture.Started, attempt => Assert.Equal(15_000, attempt.DeadlineForegroundMs));
        Assert.Equal(["succeeded", "succeeded"], capture.Finished.Select(x => x.Outcome));
        Assert.DoesNotContain("true", capture.Started.SelectMany(x => new[] { x.Journey, x.Action }));
    }

    [Fact]
    public async Task AppliedEffectWithSaveFailureKeepsSessionValueAndReportsSaveOnly()
    {
        var settings = new TestSettingsService();
        var controller = new SettingsChangeController(settings,
            (_, _) => Task.FromException(new InvalidOperationException("storage unavailable")));
        var effectApplied = false;

        var outcome = await controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), true,
            (_, _) => { effectApplied = true; return Task.FromResult(SettingsEffectResult.Success); });

        Assert.True(effectApplied);
        Assert.Equal(SettingsChangeStatus.AppliedSessionOnly, outcome.Status);
        Assert.Equal(NotificationIssueCode.SettingsNotSaved, outcome.IssueCode);
        Assert.True(settings.GetSettings().IsDarkModeEnabled);
        Assert.True(controller.HasPendingSave(nameof(Settings.IsDarkModeEnabled)));
    }

    [Fact]
    public async Task ApplyFailureWinsAndDoesNotAttemptSave()
    {
        var settings = new TestSettingsService();
        var saveCalled = false;
        var controller = new SettingsChangeController(settings, (_, _) =>
        {
            saveCalled = true;
            return Task.FromException(new InvalidOperationException("storage unavailable"));
        });

        var outcome = await controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), true,
            (_, _) => Task.FromResult(SettingsEffectResult.Failure));

        Assert.Equal(SettingsChangeStatus.ApplyFailedRestored, outcome.Status);
        Assert.Equal(NotificationIssueCode.SettingsApplyFailed, outcome.IssueCode);
        Assert.False(settings.GetSettings().IsDarkModeEnabled);
        Assert.False(saveCalled);
        Assert.False(controller.HasPendingSave(nameof(Settings.IsDarkModeEnabled)));
    }

    [Fact]
    public async Task PartialEffectIsCompensatedToVerifiedPriorValue()
    {
        var settings = new TestSettingsService();
        var effectValue = false;
        var controller = new SettingsChangeController(settings);

        var outcome = await controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), true,
            (_, _) => { effectValue = true; return Task.FromResult(SettingsEffectResult.PartialFailure); },
            (prior, _) => { effectValue = prior; return Task.FromResult(SettingsEffectResult.Success); });

        Assert.Equal(SettingsChangeStatus.ApplyFailedRestored, outcome.Status);
        Assert.False(effectValue);
        Assert.False(settings.GetSettings().IsDarkModeEnabled);
    }

    [Fact]
    public async Task FailedCompensationReturnsExplicitMixedState()
    {
        var settings = new TestSettingsService();
        var controller = new SettingsChangeController(settings);

        var outcome = await controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), true,
            (_, _) => Task.FromResult(SettingsEffectResult.PartialFailure),
            (_, _) => Task.FromResult(SettingsEffectResult.Failure));

        Assert.Equal(SettingsChangeStatus.MixedState, outcome.Status);
        Assert.Equal(NotificationIssueCode.SettingsApplyFailed, outcome.IssueCode);
        Assert.False(settings.GetSettings().IsDarkModeEnabled);
        Assert.False(outcome.IsPersisted);
    }

    [Fact]
    public async Task OlderEffectCompletionCannotReplaceNewerRequestAndIsCompensated()
    {
        var settings = new TestSettingsService();
        var controller = new SettingsChangeController(settings);
        var effectValue = false;
        var oldEffectCompletion = new TaskCompletionSource<SettingsEffectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldChange = controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), true,
            async (_, _) =>
            {
                var result = await oldEffectCompletion.Task;
                effectValue = true;
                return result;
            },
            (prior, _) => { effectValue = prior; return Task.FromResult(SettingsEffectResult.Success); });

        var newChange = await controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), false,
            (_, _) => { effectValue = false; return Task.FromResult(SettingsEffectResult.Success); });
        oldEffectCompletion.SetResult(SettingsEffectResult.Success);
        var oldOutcome = await oldChange;

        Assert.Equal(SettingsChangeStatus.Applied, newChange.Status);
        Assert.Equal(SettingsChangeStatus.Superseded, oldOutcome.Status);
        Assert.False(effectValue);
        Assert.False(settings.GetSettings().IsDarkModeEnabled);
    }

    [Fact]
    public async Task OldSaveFailureAfterNewerSuccessDoesNotOverwriteOrReportCurrentState()
    {
        var settings = new TestSettingsService();
        var oldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCalls = 0;
        var controller = new SettingsChangeController(settings, async (_, _) =>
        {
            var call = Interlocked.Increment(ref saveCalls);
            if (call == 1) await oldSave.Task;
            else if (call == 2) return;
        });

        var oldChange = controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), true,
            (_, _) => Task.FromResult(SettingsEffectResult.Success));
        await Task.Yield();
        var newChange = await controller.ChangeAsync(
            nameof(Settings.IsDarkModeEnabled), false,
            (_, _) => Task.FromResult(SettingsEffectResult.Success));
        oldSave.SetException(new InvalidOperationException("stale save failure"));
        var oldOutcome = await oldChange;

        Assert.Equal(SettingsChangeStatus.Applied, newChange.Status);
        Assert.Equal(SettingsChangeStatus.Superseded, oldOutcome.Status);
        Assert.False(settings.GetSettings().IsDarkModeEnabled);
        Assert.False(controller.HasPendingSave(nameof(Settings.IsDarkModeEnabled)));
    }

    [Fact]
    public async Task SuccessfulUnrelatedSaveDoesNotResolvePreviousStorageFailure()
    {
        var settings = new TestSettingsService();
        var failFirst = true;
        var controller = new SettingsChangeController(settings, (_, _) =>
        {
            if (failFirst)
            {
                failFirst = false;
                return Task.FromException(new InvalidOperationException("storage unavailable"));
            }

            return Task.CompletedTask;
        });

        await controller.ChangeAsync(nameof(Settings.IsDarkModeEnabled), true,
            (_, _) => Task.FromResult(SettingsEffectResult.Success));
        Assert.True(controller.HasPendingSave(nameof(Settings.IsDarkModeEnabled)));

        await controller.ChangeAsync(nameof(Settings.AreCheckpointsVisible), false,
            (_, _) => Task.FromResult(SettingsEffectResult.Success));
        Assert.True(controller.HasPendingSave(nameof(Settings.IsDarkModeEnabled)));
        Assert.False(controller.HasPendingSave(nameof(Settings.AreCheckpointsVisible)));

        await controller.ChangeAsync(nameof(Settings.IsDarkModeEnabled), false,
            (_, _) => Task.FromResult(SettingsEffectResult.Success));
        Assert.False(controller.HasPendingSave(nameof(Settings.IsDarkModeEnabled)));
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

    sealed class RecordingAttempts : IClientExperienceAttemptCapture
    {
        public List<StartedAttempt> Started { get; } = [];
        public List<FinishedAttempt> Finished { get; } = [];

        public Guid StartAttempt(string citySlug, string journey, long deadlineForegroundMs, Guid? cityActivationId = null,
            Guid? parentActionId = null, string? action = null)
        {
            var id = Guid.NewGuid();
            Started.Add(new(id, citySlug, journey, deadlineForegroundMs, parentActionId, action));
            return id;
        }

        public void FinishAttempt(Guid attemptId, string citySlug, string journey, string outcome, string reason, Guid? cityActivationId = null)
            => Finished.Add(new(attemptId, outcome));

        public sealed record StartedAttempt(Guid Id, string CitySlug, string Journey, long DeadlineForegroundMs, Guid? ParentActionId, string? Action);
        public sealed record FinishedAttempt(Guid Id, string Outcome);
    }
}
