using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public interface IPageVisibilityJsInterop : IAsyncDisposable
{
    Task AddVisibilityChangeListenerAsync(Action<bool> onVisibilityChanged);
    Task RemoveVisibilityChangeListenerAsync();
    Task<bool> GetHiddenAsync();
}
