using ChefKnifeStudios.MartaJazz.Client.Shared.Resources;
using MatBlazor;
using Microsoft.Extensions.Localization;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services;

public interface IToastService
{
    void ShowSuccess(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowError(string message, string? title = null);
}

public class ToastService(IMatToaster matToaster, IStringLocalizer<RouteFilterResources> localizer) : IToastService
{
    public void ShowSuccess(string message, string? title = null)
        => Show(MatToastType.Success, title ?? localizer["ToastSuccessTitle"], message);
    public void ShowWarning(string message, string? title = null)
        => Show(MatToastType.Warning, title ?? localizer["ToastWarningTitle"], message);
    public void ShowError(string message, string? title = null)
        => Show(MatToastType.Danger, title ?? localizer["ToastErrorTitle"], message);

    void Show(MatToastType type, string title, string message, string icon = "")
    {
        matToaster.Add(message, type, title, icon);
    }
}
