using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public interface IPageVisibilityJsInterop : IAsyncDisposable
{
    event Action<bool>? VisibilityChanged;
    Task AddVisibilityChangeListenerAsync(Action<bool> onVisibilityChanged);
    Task RemoveVisibilityChangeListenerAsync();
    Task<bool> GetHiddenAsync();
}
