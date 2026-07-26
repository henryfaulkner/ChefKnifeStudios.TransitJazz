using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public interface IOutsideClickJsInterop : IAsyncDisposable
{
    Task AddOutsideClickListenerAsync(string elementId, Action callback);
    Task RemoveOutsideClickListenerAsync(string elementId);
}
