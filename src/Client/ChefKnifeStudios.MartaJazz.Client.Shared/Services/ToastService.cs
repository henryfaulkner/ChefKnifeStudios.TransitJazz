using MatBlazor;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services;

public interface IToastService
{
    void ShowSuccess(string message, string title = "SUCCESS");
    void ShowWarning(string message, string title = "WARNING");
    void ShowError(string message, string title = "ERROR");
}

public class ToastService(IMatToaster matToaster) : IToastService
{
    public void ShowSuccess(string message, string title = "SUCCESS")
        => Show(MatToastType.Success, title, message);
    public void ShowWarning(string message, string title = "WARNING")
        => Show(MatToastType.Warning, title, message);
    public void ShowError(string message, string title = "ERROR")
        => Show(MatToastType.Danger, title, message);


    void Show(MatToastType type, string title, string message, string icon = "")
    {
        matToaster.Add(message, type, title, icon);
    }
}
