using System;
using System.Numerics;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public interface IViewportSizeJsInterop : IAsyncDisposable
{
    ValueTask RegisterViewportSizeAsync();
    IDisposable AddViewportSizeChangeCallback(Action<Vector2> callback);
}
