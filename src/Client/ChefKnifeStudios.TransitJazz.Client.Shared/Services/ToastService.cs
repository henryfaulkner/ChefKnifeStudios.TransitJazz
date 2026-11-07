using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using MatBlazor;
using Microsoft.Extensions.Localization;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

public interface IToastService
{
    void ShowSuccess(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowError(string message, string? title = null);
}

public sealed class ToastService :
    IToastService,
    INotificationPresenter,
    IDisposable
{
    readonly object _sync = new();
    readonly IMatToaster _matToaster;
    readonly IStringLocalizer<RouteFilterResources> _localizer;
    MatToast? _currentHandle;
    long _currentPresentationId;
    long _latestPresentationId;
    bool _disposed;

    public ToastService(IMatToaster matToaster, IStringLocalizer<RouteFilterResources> localizer)
    {
        _matToaster = matToaster ?? throw new ArgumentNullException(nameof(matToaster));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _matToaster.OnToastsUpdated += OnToastsUpdated;
    }

    public void ShowSuccess(string message, string? title = null)
        => Show(MatToastType.Success, title ?? _localizer["ToastSuccessTitle"], message);

    public void ShowWarning(string message, string? title = null)
        => Show(MatToastType.Warning, title ?? _localizer["ToastWarningTitle"], message);

    public void ShowError(string message, string? title = null)
        => Show(MatToastType.Danger, title ?? _localizer["ToastErrorTitle"], message);

    public ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_disposed || presentationId <= _latestPresentationId)
            {
                return ValueTask.FromResult(false);
            }

            _latestPresentationId = presentationId;
            RemoveCurrentLocked();

            var message = BuildMessage(summary);
            var title = _localizer[summary.SeverityKey].Value;
            var type = summary.Severity == NotificationSeverity.Warning ? MatToastType.Warning : MatToastType.Danger;
            var handle = _matToaster.Add(message, type, title, string.Empty, options =>
            {
                options.RequireInteraction = true;
                options.ShowTransitionDuration = 0;
                options.HideTransitionDuration = 0;
                options.ShowProgressBar = false;
                options.ShowCloseButton = false;
            });

            if (handle is null || !_matToaster.Toasts.Contains(handle))
            {
                return ValueTask.FromResult(false);
            }

            _currentHandle = handle;
            _currentPresentationId = presentationId;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_disposed || presentationId != _currentPresentationId)
            {
                return ValueTask.CompletedTask;
            }

            RemoveCurrentLocked();
        }

        return ValueTask.CompletedTask;
    }

    public string FormatAnnouncement(NotificationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return $"{_localizer[summary.SeverityKey].Value}. {BuildMessage(summary)}";
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _matToaster.OnToastsUpdated -= OnToastsUpdated;
            RemoveCurrentLocked();
        }
    }

    void Show(MatToastType type, string title, string message)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _matToaster.Add(message, type, title, string.Empty);
        }
    }

    string BuildMessage(NotificationSummary summary)
    {
        string impact;
        if (summary.Kind == NotificationSummaryKind.MultipleCapabilities && summary.CapabilityKeys.Count == 2)
        {
            var capabilities = summary.CapabilityKeys.Select(key => _localizer[key].Value).Cast<object>().ToArray();
            impact = _localizer[summary.ImpactKey, capabilities].Value;
        }
        else
        {
            impact = _localizer[summary.ImpactKey].Value;
        }

        var parts = new List<string> { impact };
        if (summary.OfflineQualifierKey is not null)
        {
            parts.Add(_localizer[summary.OfflineQualifierKey].Value);
        }

        parts.Add(_localizer[summary.ActionKey].Value);
        return string.Join(" ", parts);
    }

    void RemoveCurrentLocked()
    {
        var handle = _currentHandle;
        _currentHandle = null;
        _currentPresentationId = 0;
        if (handle is not null && _matToaster.Toasts.Contains(handle))
        {
            _matToaster.Remove(handle);
        }
    }

    void OnToastsUpdated()
    {
        lock (_sync)
        {
            if (_currentHandle is not null && !_matToaster.Toasts.Contains(_currentHandle))
            {
                _currentHandle = null;
                _currentPresentationId = 0;
            }
        }
    }
}
