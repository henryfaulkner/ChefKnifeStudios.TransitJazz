using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

/// <summary>
/// The hidden-tab-pause delivery boundary (feature 051, US3): pause/resume the SignalR city
/// group membership, plus the last-pushed desired-delivery flag the Reconnected handler
/// consults so it never blind-rejoins a session the gate wants paused (contract C3). The gate
/// owns this interface and pushes DesiredDelivery into it before issuing Pause/Resume — the
/// control MUST NOT hold a reference back to the gate (one-directional dependency,
/// data-model §4).
/// </summary>
public interface IDeliveryControl
{
    bool DesiredDelivery { set; }
    Task PauseAsync();
    Task ResumeAsync();
}

/// <summary>
/// Computes desiredDelivery = !(hidden &amp;&amp; !audioEnabled) from visibility/mute events and
/// reconciles the delivery control toward it. A single in-flight guard serializes hub calls;
/// reconciliation re-runs after each completion so a burst of rapid toggles settles on the
/// last-computed desired state without ever running two delivery-control calls concurrently
/// (data-model §4 invariants). No browser/JS dependency — page-visibility.js and TransitMap
/// wiring stay thin adapters over this class (test-plan Phase 2 seam).
/// </summary>
public sealed class AttentionGate(IDeliveryControl deliveryControl)
{
    readonly SemaphoreSlim _lock = new(1, 1);
    bool _joined = true; // TransitMap starts already joined via SignalRNotificationService.InitAsync

    public Task OnVisibilityChangedAsync(bool hidden, bool audioEnabled) =>
        ReconcileAsync(hidden, audioEnabled);

    public Task OnAudioChangedAsync(bool hidden, bool audioEnabled) =>
        ReconcileAsync(hidden, audioEnabled);

    async Task ReconcileAsync(bool hidden, bool audioEnabled)
    {
        var desired = !(hidden && !audioEnabled);
        deliveryControl.DesiredDelivery = desired;

        await _lock.WaitAsync();
        try
        {
            if (desired == _joined) return;

            if (desired)
                await deliveryControl.ResumeAsync();
            else
                await deliveryControl.PauseAsync();

            _joined = desired;
        }
        finally
        {
            _lock.Release();
        }
    }
}
