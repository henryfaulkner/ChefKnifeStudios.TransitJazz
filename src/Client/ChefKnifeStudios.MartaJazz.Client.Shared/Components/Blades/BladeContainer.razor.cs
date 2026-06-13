using ChefKnifeStudios.MartaJazz.Client.Core.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;
using Microsoft.AspNetCore.Components;
using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components.Blades;

public partial class BladeContainer
{
    const int MinOpenDurationMs = 300;

    [Parameter] public RenderFragment? ContentFragment { get; set; }

    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] IOutsideClickJsInterop OutsideClickJsInterop { get; set; } = null!;

    readonly string _elementId = $"blade-{Guid.NewGuid()}";
    bool _isOpen;
    DateTime _lastOpenedUtc = DateTime.MinValue;

    public async Task Open()
    {
        _lastOpenedUtc = DateTime.UtcNow;
        _isOpen = true;
        await OutsideClickJsInterop.AddOutsideClickListenerAsync(_elementId, HandleClosePressed);
        await InvokeAsync(StateHasChanged);
    }

    public async Task Close()
    {
        if ((DateTime.UtcNow - _lastOpenedUtc).TotalMilliseconds < MinOpenDurationMs) return;
        _isOpen = false;
        await OutsideClickJsInterop.RemoveOutsideClickListenerAsync(_elementId);
        await InvokeAsync(StateHasChanged);
    }

    void HandleClosePressed()
    {
        EventNotificationService.PostEvent(this, new BladeEventArgs { Type = BladeEventArgs.Types.Close });
    }

    public async ValueTask DisposeAsync()
    {
        await OutsideClickJsInterop.RemoveOutsideClickListenerAsync(_elementId);
    }
}
