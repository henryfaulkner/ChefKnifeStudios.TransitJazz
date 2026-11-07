using System.Reflection;
using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using MatBlazor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class ToastServiceTests
{
    [Fact]
    public void LegacyWarningAndErrorRequestsKeepTheirMatToastTypes()
    {
        var toaster = DispatchProxy.Create<IMatToaster, RecordingMatToaster>();
        var recorder = (RecordingMatToaster)(object)toaster;
        var service = new ToastService(toaster, new TestLocalizer());

        service.ShowWarning("warning body", "warning title");
        service.ShowError("error body", "error title");

        Assert.Collection(
            recorder.AddRequests,
            warning =>
            {
                Assert.Equal("warning body", warning.Message);
                Assert.Equal("warning title", warning.Title);
                Assert.Equal(MatToastType.Warning, warning.Type);
            },
            error =>
            {
                Assert.Equal("error body", error.Message);
                Assert.Equal("error title", error.Title);
                Assert.Equal(MatToastType.Danger, error.Type);
            });
    }

    [Fact]
    public async Task ToastServiceCanPresentAndOwnAHandleForTheNotificationCoordinator()
    {
        var services = new ServiceCollection();
        services.AddMatToaster(new MatToastConfiguration { MaxDisplayedToasts = 1, PreventDuplicates = false });
        using var provider = services.BuildServiceProvider();
        var toaster = provider.GetRequiredService<IMatToaster>();
        var service = new ToastService(toaster, new TestLocalizer());

        var presenter = Assert.IsAssignableFrom<INotificationPresenter>(service);
        var observedCounts = new List<int>();
        toaster.OnToastsUpdated += () => observedCounts.Add(toaster.Toasts.Count);

        Assert.True(await presenter.PresentAsync(1, Summary(NotificationSeverity.Warning, NotificationIssueCode.LiveInterrupted), CancellationToken.None));
        var firstHandle = Assert.Single(toaster.Toasts);
        Assert.Equal(MatToastType.Warning, firstHandle.Options.Type);
        Assert.True(firstHandle.Options.RequireInteraction);
        Assert.Equal(0, firstHandle.Options.ShowTransitionDuration);
        Assert.Equal(0, firstHandle.Options.HideTransitionDuration);
        Assert.False(firstHandle.Options.ShowProgressBar);
        Assert.False(firstHandle.Options.ShowCloseButton);
        Assert.True(await presenter.PresentAsync(2, Summary(NotificationSeverity.Error, NotificationIssueCode.LiveStopped), CancellationToken.None));

        var replacementHandle = Assert.Single(toaster.Toasts);
        Assert.NotSame(firstHandle, replacementHandle);
        Assert.Equal(MatToastType.Danger, replacementHandle.Options.Type);

        await presenter.RemoveAsync(1, CancellationToken.None);
        Assert.Contains(replacementHandle, toaster.Toasts);
        await presenter.RemoveAsync(2, CancellationToken.None);
        Assert.Empty(toaster.Toasts);
        Assert.Equal(new[] { 1, 0, 1, 0 }, observedCounts);
    }

    [Fact]
    public async Task NotificationRegistrationKeepsSingletonCoordinatorIndependentOfScopedPresenter()
    {
        var services = new ServiceCollection();
        services.AddMatToaster(new MatToastConfiguration { MaxDisplayedToasts = 1, PreventDuplicates = false });
        services.AddSingleton<IStringLocalizer<RouteFilterResources>>(new TestLocalizer());
        services.AddNotificationPresentation();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var coordinator = provider.GetRequiredService<NotificationCoordinator>();
        Assert.NotNull(coordinator);

        using var firstScope = provider.CreateScope();
        var presenter = firstScope.ServiceProvider.GetRequiredService<INotificationPresenter>();
        Assert.Same(presenter, firstScope.ServiceProvider.GetRequiredService<IToastService>());
        Assert.Same(presenter, firstScope.ServiceProvider.GetRequiredService<ToastService>());

        using var secondScope = provider.CreateScope();
        Assert.NotSame(presenter, secondScope.ServiceProvider.GetRequiredService<INotificationPresenter>());
    }

    [Fact]
    public void InstalledMatBlazor210ReturnsRemovableToastHandlesAndMaximumDoesNotCapQueuedItems()
    {
        var services = new ServiceCollection();
        services.AddMatToaster(new MatToastConfiguration
        {
            MaxDisplayedToasts = 1,
            PreventDuplicates = false,
            NewestOnTop = true
        });

        using var provider = services.BuildServiceProvider();
        var toaster = provider.GetRequiredService<IMatToaster>();
        var add = typeof(IMatToaster).GetMethods().Single(method => method.Name == "Add");
        Assert.Equal(typeof(MatToast), add.ReturnType);

        var first = Assert.IsType<MatToast>(add.Invoke(toaster, ["first", MatToastType.Warning, "title", "", null]));
        var second = Assert.IsType<MatToast>(add.Invoke(toaster, ["second", MatToastType.Danger, "title", "", null]));
        var closeCount = 0;
        first.OnClose += _ => closeCount++;

        var packageToasts = Assert.IsAssignableFrom<System.Collections.ICollection>(toaster.Toasts);
        Assert.Equal(2, packageToasts.Count);
        Assert.Contains(first, toaster.Toasts);
        Assert.Contains(second, toaster.Toasts);

        toaster.Remove(first);

        Assert.DoesNotContain(first, toaster.Toasts);
        Assert.Contains(second, toaster.Toasts);
        Assert.Equal(0, closeCount);
    }

    public class RecordingMatToaster : DispatchProxy
    {
        public List<AddRequest> AddRequests { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "Add")
            {
                AddRequests.Add(new AddRequest(
                    (string)args![0]!,
                    (MatToastType)args[1]!,
                    (string)args[2]!));
            }

            return null;
        }
    }

    public sealed record AddRequest(string Message, MatToastType Type, string Title);

    static NotificationSummary Summary(NotificationSeverity severity, NotificationIssueCode code) =>
        new(
            severity,
            NotificationSummaryKind.SingleCapability,
            severity == NotificationSeverity.Warning ? "ToastSeverityWarning" : "ToastSeverityError",
            code == NotificationIssueCode.LiveInterrupted ? "ToastLiveInterrupted" : "ToastLiveStopped",
            "ToastActionTryAgain",
            "ToastClose",
            [NotificationCapability.Live],
            [code],
            null);

    sealed class TestLocalizer : IStringLocalizer<RouteFilterResources>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
