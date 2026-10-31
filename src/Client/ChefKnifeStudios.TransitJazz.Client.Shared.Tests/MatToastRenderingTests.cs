using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using MatBlazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class MatToastRenderingTests
{
    [Fact]
    public async Task InstalledMatToastContainerRendersTheOwnedWarningThroughTheRealPackage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMatToaster(new MatToastConfiguration { MaxDisplayedToasts = 1, PreventDuplicates = false });
        services.AddSingleton<IJSRuntime>(DispatchProxy.Create<IJSRuntime, NoOpJsRuntime>());
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<MatToastContainer>();
            using var toastService = new ToastService(
                provider.GetRequiredService<IMatToaster>(), new TestLocalizer());
            var summary = new NotificationSummary(
                NotificationSeverity.Warning,
                NotificationSummaryKind.SingleCapability,
                "ToastSeverityWarning",
                "ToastLiveInterrupted",
                "ToastActionWaitForReconnect",
                "ToastClose",
                [NotificationCapability.Live],
                [NotificationIssueCode.LiveInterrupted],
                null);

            Assert.True(await toastService.PresentAsync(1, summary, CancellationToken.None));
            await Task.Yield();
            return root.ToHtmlString();
        });

        Assert.Contains("mat-toast", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Live updates are interrupted", html, StringComparison.Ordinal);
    }

    sealed class TestLocalizer : IStringLocalizer<RouteFilterResources>
    {
        public LocalizedString this[string name] => new(name, name switch
        {
            "ToastSeverityWarning" => "Warning",
            "ToastLiveInterrupted" => "Live updates are interrupted.",
            "ToastActionWaitForReconnect" => "Live updates will reconnect automatically.",
            _ => name
        });

        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    public class NoOpJsRuntime : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType == typeof(ValueTask)
                ? ValueTask.CompletedTask
                : targetMethod?.ReturnType.IsGenericType == true
                    ? Activator.CreateInstance(targetMethod.ReturnType, Activator.CreateInstance(targetMethod.ReturnType.GenericTypeArguments[0]))
                    : null;
    }
}
