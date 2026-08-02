using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

// P2-U1..P2-U5 (051 US3): AttentionGate is a plain class over an injected IDeliveryControl —
// no browser/JS dependency — so the riskiest logic (async reconciliation under rapid toggling)
// is provable at Tier 0. Spy delivery control per house spy pattern (WorkerTransitHubTests /
// FakeApplicationViewModel): hand-written fake, "Last*"/count fields, no mocking framework.
public class AttentionGateTests
{
    [Fact]
    public async Task HiddenWhileMuted_Pauses()
    {
        var spy = new SpyDeliveryControl();
        var gate = new AttentionGate(spy);

        await gate.OnVisibilityChangedAsync(hidden: true, audioEnabled: false);

        Assert.Equal(1, spy.PauseCallCount);
        Assert.Equal(0, spy.ResumeCallCount);
    }

    [Fact]
    public async Task HiddenWhileAudioPlaying_DoesNotPause()
    {
        var spy = new SpyDeliveryControl();
        var gate = new AttentionGate(spy);

        await gate.OnVisibilityChangedAsync(hidden: true, audioEnabled: true);

        Assert.Equal(0, spy.PauseCallCount);
        Assert.Equal(0, spy.ResumeCallCount);
    }

    [Fact]
    public async Task VisibleAfterPause_Resumes()
    {
        var spy = new SpyDeliveryControl();
        var gate = new AttentionGate(spy);
        await gate.OnVisibilityChangedAsync(hidden: true, audioEnabled: false);

        await gate.OnVisibilityChangedAsync(hidden: false, audioEnabled: false);

        Assert.Equal(1, spy.PauseCallCount);
        Assert.Equal(1, spy.ResumeCallCount);
    }

    [Fact]
    public async Task MuteWhileHidden_Pauses()
    {
        var spy = new SpyDeliveryControl();
        var gate = new AttentionGate(spy);
        await gate.OnVisibilityChangedAsync(hidden: true, audioEnabled: true); // hidden, unmuted — streaming

        await gate.OnAudioChangedAsync(hidden: true, audioEnabled: false); // now muted while still hidden

        Assert.Equal(1, spy.PauseCallCount);
    }

    [Fact]
    public async Task UnmuteWhileHidden_Resumes()
    {
        var spy = new SpyDeliveryControl();
        var gate = new AttentionGate(spy);
        await gate.OnVisibilityChangedAsync(hidden: true, audioEnabled: false); // paused

        await gate.OnAudioChangedAsync(hidden: true, audioEnabled: true); // unmuted while still hidden

        Assert.Equal(1, spy.PauseCallCount);
        Assert.Equal(1, spy.ResumeCallCount);
    }

    // P2-U4: reconnect must consult the gate's last-pushed DesiredDelivery, never blind-rejoin.
    // The control reads its own last-pushed flag — no reference from control back to gate.
    [Fact]
    public async Task ReconnectWhilePaused_DoesNotRejoin()
    {
        var spy = new SpyDeliveryControl();
        var gate = new AttentionGate(spy);
        await gate.OnVisibilityChangedAsync(hidden: true, audioEnabled: false); // pushes DesiredDelivery=false, pauses

        await spy.RaiseReconnectedAsync();

        Assert.Equal(0, spy.ResumeCallCount);
    }

    [Fact]
    public async Task ReconnectWhileActive_Rejoins()
    {
        var spy = new SpyDeliveryControl();
        var gate = new AttentionGate(spy);
        await gate.OnVisibilityChangedAsync(hidden: false, audioEnabled: false); // desired=true (visible)

        await spy.RaiseReconnectedAsync();

        Assert.Equal(1, spy.ResumeCallCount);
    }

    // P2-U5: a burst of rapid hidden/visible flips while the first call is in flight — the test
    // holds the first PauseAsync open via a TaskCompletionSource it releases itself (event-based
    // sync per reliability.md, zero sleeps). Final state must equal the last-computed desired
    // state, and at no point may two delivery-control calls be in flight simultaneously.
    [Fact]
    public async Task RapidToggleBurst_SettlesOnFinalDesiredState_WithSingleInFlightCall()
    {
        var spy = new SpyDeliveryControl { HoldFirstCall = true };
        var gate = new AttentionGate(spy);

        var firstCall = gate.OnVisibilityChangedAsync(hidden: true, audioEnabled: false); // blocks on TCS

        // Queue further toggles while the first call is still in flight — reconciliation must
        // serialize these, never run two delivery-control calls concurrently.
        var t2 = gate.OnVisibilityChangedAsync(hidden: false, audioEnabled: false); // desired -> true
        var t3 = gate.OnVisibilityChangedAsync(hidden: true, audioEnabled: false);  // desired -> false
        var t4 = gate.OnVisibilityChangedAsync(hidden: false, audioEnabled: false); // desired -> true (final)

        Assert.False(spy.MaxConcurrentInFlightExceededOne);

        spy.ReleaseFirstCall();
        await firstCall;
        await Task.WhenAll(t2, t3, t4);

        Assert.False(spy.MaxConcurrentInFlightExceededOne);
        Assert.True(spy.DesiredDeliveryLastPushed);
        Assert.True(spy.LastAppliedDelivery);
    }

    // Hand-rolled spy: no mocking framework, per house convention. Tracks call counts and the
    // last-pushed DesiredDelivery flag independently of the gate (one-directional dependency —
    // the control never holds a reference back to the gate).
    sealed class SpyDeliveryControl : IDeliveryControl
    {
        public int PauseCallCount { get; private set; }
        public int ResumeCallCount { get; private set; }
        public bool DesiredDeliveryLastPushed { get; private set; }
        public bool LastAppliedDelivery { get; private set; }
        public bool HoldFirstCall { get; set; }
        public bool MaxConcurrentInFlightExceededOne { get; private set; }

        int _inFlight;
        TaskCompletionSource? _firstCallGate;

        public bool DesiredDelivery
        {
            set => DesiredDeliveryLastPushed = value;
        }

        public async Task PauseAsync()
        {
            await GuardedCallAsync();
            PauseCallCount++;
            LastAppliedDelivery = false;
        }

        public async Task ResumeAsync()
        {
            await GuardedCallAsync();
            ResumeCallCount++;
            LastAppliedDelivery = true;
        }

        async Task GuardedCallAsync()
        {
            var concurrent = System.Threading.Interlocked.Increment(ref _inFlight);
            if (concurrent > 1) MaxConcurrentInFlightExceededOne = true;

            if (HoldFirstCall && _firstCallGate is null)
            {
                _firstCallGate = new TaskCompletionSource();
                await _firstCallGate.Task;
            }

            System.Threading.Interlocked.Decrement(ref _inFlight);
        }

        public void ReleaseFirstCall() => _firstCallGate?.TrySetResult();

        // Test seam mirroring HubConnection.Reconnected: the production ResumeAsync would be
        // invoked by SignalRNotificationService's own Reconnected handler, which is expected to
        // consult _desiredDelivery internally (contract C3) rather than blind-rejoin — simulated
        // here by only calling ResumeAsync when the last-pushed desired flag is true.
        public Task RaiseReconnectedAsync() => DesiredDeliveryLastPushed ? ResumeAsync() : Task.CompletedTask;
    }
}
