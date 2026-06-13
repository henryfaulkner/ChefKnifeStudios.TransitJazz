using ChefKnifeStudios.MartaJazz.Client.Core.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;
using ChefKnifeStudios.MartaJazz.Client.Shared.Models;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components.Blades;

public partial class SettingsBlade : IDisposable
{
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] ISettingsService SettingsService { get; set; } = null!;
    [Inject] ILogger<SettingsBlade> Logger { get; set; } = null!;

    BladeContainer? _bladeContainer;
    Settings _settings = new();

    protected override void OnInitialized()
    {
        _settings = SettingsService.GetSettings();
        EventNotificationService.EventReceived += HandleEventReceived;
    }

    void HandleEventReceived(object sender, IEventArgs e)
    {
        if (e is not BladeEventArgs blade) return;

        if (blade.Type == BladeEventArgs.Types.Settings)
        {
            _settings = SettingsService.GetSettings();
            InvokeAsync(async () => await (_bladeContainer?.Open() ?? System.Threading.Tasks.Task.CompletedTask));
        }
        else
        {
            InvokeAsync(async () => await (_bladeContainer?.Close() ?? System.Threading.Tasks.Task.CompletedTask));
        }
    }

    void HandleSettingPressed(string propertyName, bool value)
    {
        try
        {
            SettingsService.SetSettingValue(propertyName, value);
            _settings = SettingsService.GetSettings();

            IEventArgs? effectEvent = propertyName switch
            {
                nameof(Settings.IsAudioEnabled) => new AudioSettingChangedEventArgs { IsAudioEnabled = value },
                nameof(Settings.IsStreetsBasemap) => new GisSettingChangedEventArgs { IsStreetsBasemap = value },
                nameof(Settings.AreCheckpointsVisible) => new CheckpointVisibilityChangedEventArgs { AreCheckpointsVisible = value },
                _ => null
            };

            if (effectEvent is not null)
                EventNotificationService.PostEvent(this, effectEvent);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SettingsBlade.HandleSettingPressed: failed for property {PropertyName}", propertyName);
        }
    }

    public void Dispose()
    {
        EventNotificationService.EventReceived -= HandleEventReceived;
    }
}
