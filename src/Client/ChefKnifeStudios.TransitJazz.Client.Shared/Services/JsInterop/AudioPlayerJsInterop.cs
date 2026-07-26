using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public interface IAudioPlayerJsInterop
{
    Task PlayAsync(string soundUrl);
}

public class AudioPlayerJsInterop : IAudioPlayerJsInterop, IAsyncDisposable
{
    readonly Lazy<Task<IJSObjectReference>> moduleTask;
    readonly ILogger<AudioPlayerJsInterop> _logger;

    public AudioPlayerJsInterop(
        IJSRuntime jsRuntime,
        ILogger<AudioPlayerJsInterop> logger)
    {
        _logger = logger;
        moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/audioPlayerJsInterop.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async Task PlayAsync(string soundUrl)
    {
        if (string.IsNullOrWhiteSpace(soundUrl)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("play", soundUrl);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async ValueTask DisposeAsync()
    {
        if (moduleTask.IsValueCreated)
        {
            var module = await moduleTask.Value;
            await module.DisposeAsync();
        }
    }

    void LogError(Exception ex)
    {
        _logger.LogError(
            ex,
            "AudioPlayerJsInterop encountered a JavaScript error: {errorMessage}",
            ex.Message);
    }
}
