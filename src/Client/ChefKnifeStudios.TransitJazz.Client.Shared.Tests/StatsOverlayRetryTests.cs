using System.Net;
using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsOverlayRetryTests
{
    [Fact]
    public void CooldownStartsAtSixtySecondsDoublesToFiveMinutesAndSurvivesScopeChanges()
    {
        var policy = new StatsOverlayRetryPolicy();
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddSeconds(60), policy.RecordFailure(now));
        Assert.True(policy.IsCoolingDown(now.AddSeconds(59)));
        Assert.False(policy.IsCoolingDown(now.AddSeconds(60)));

        // Scope and route changes reuse the same session policy; they do not reset the active cooldown.
        var afterScopeChange = now.AddSeconds(60);
        Assert.Equal(afterScopeChange.AddSeconds(120), policy.RecordFailure(afterScopeChange));

        var afterSecondCooldown = afterScopeChange.AddSeconds(120);
        Assert.Equal(afterSecondCooldown.AddSeconds(240), policy.RecordFailure(afterSecondCooldown));
        var afterThirdCooldown = afterSecondCooldown.AddSeconds(240);
        Assert.Equal(afterThirdCooldown.AddMinutes(5), policy.RecordFailure(afterThirdCooldown));
    }

    [Fact]
    public void LongerServerRetryAfterWinsAndSuccessResetsBackoff()
    {
        var policy = new StatsOverlayRetryPolicy();
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddMinutes(9), policy.RecordFailure(now, TimeSpan.FromMinutes(9)));
        policy.RecordSuccess();
        Assert.Null(policy.RetryAtUtc);
        Assert.Equal(now.AddDays(1).AddSeconds(60), policy.RecordFailure(now.AddDays(1)));
    }

    [Fact]
    public void BackoffBoundariesAreSixtyOneTwentyTwoFortyThreeHundredAndThreeHundredSeconds()
    {
        var policy = new StatsOverlayRetryPolicy();
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var expected = new[] { 60, 120, 240, 300, 300 };

        foreach (var seconds in expected)
        {
            var retryAt = policy.RecordFailure(now);
            Assert.Equal(now.AddSeconds(seconds), retryAt);
            Assert.True(policy.IsCoolingDown(retryAt.AddTicks(-1)));
            Assert.False(policy.IsCoolingDown(retryAt));
            now = retryAt;
        }
    }

    [Theory]
    [InlineData("read_timeout", "read_timeout")]
    [InlineData("busy", "busy")]
    [InlineData("rate_limited", "rate_limited")]
    [InlineData(null, "retry_wait")]
    [InlineData("", "retry_wait")]
    public void CooldownDoesNotInventRateLimitForAnEarlierFailure(string? previousCode, string expectedCode)
    {
        Assert.Equal(expectedCode, StatsOverlayRetryPolicy.CooldownProblemCode(previousCode));
    }

    [Fact]
    public void ScopeRetryAndVisibleRestorationAttemptsPreserveFailureCauseAndDoNotAdmitReads()
    {
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var retry = new StatsOverlayRetryPolicy();
        var gate = new StatsOverlayRequestGate();
        var errorCode = "read_timeout";
        var retryAt = retry.RecordFailure(now, TimeSpan.FromSeconds(90));

        foreach (var attempt in new[]
        {
            (Name: "scope change", At: now.AddSeconds(10)),
            (Name: "manual retry", At: now.AddSeconds(60)),
            (Name: "visible restoration", At: now.AddSeconds(89))
        })
        {
            Assert.Equal(StatsOverlayRequestAdmission.CoolingDown,
                StatsOverlayRequestAdmissionPolicy.TryBegin(gate, retry, generation: 7, attempt.At));
            Assert.Equal("read_timeout", StatsOverlayRetryPolicy.CooldownProblemCode(errorCode));
            Assert.Equal(retryAt, retry.RetryAtUtc);
            Assert.False(gate.HasActiveRequest);
        }

        Assert.Equal(StatsOverlayRequestAdmission.Started,
            StatsOverlayRequestAdmissionPolicy.TryBegin(gate, retry, generation: 7, retryAt));
        Assert.True(gate.HasActiveRequest);
    }

    [Fact]
    public void LatestCanceledSelectionWaitsForCooldownThenSuccessResetsTheBackoff()
    {
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var retry = new StatsOverlayRetryPolicy();
        var gate = new StatsOverlayRequestGate();
        var retryAt = retry.RecordFailure(now, TimeSpan.FromSeconds(90));

        Assert.True(gate.TryBegin(5)); // canceled HTTP request still owns the client slot
        gate.QueueLatest(6, retainSnapshot: false); // latest route selection
        var pending = gate.Complete(5, 6);
        Assert.Equal(new PendingStatsOverlayRequest(6, RetainSnapshot: false), pending);
        Assert.Equal(StatsOverlayRequestAdmission.CoolingDown,
            StatsOverlayRequestAdmissionPolicy.TryBegin(gate, retry, pending!.Value.Generation, retryAt.AddTicks(-1)));
        Assert.False(gate.HasActiveRequest);

        Assert.Equal(StatsOverlayRequestAdmission.Started,
            StatsOverlayRequestAdmissionPolicy.TryBegin(gate, retry, pending.Value.Generation, retryAt));
        Assert.Equal(6, gate.ActiveGeneration);
        Assert.Null(gate.Complete(6, 6));
        retry.RecordSuccess();
        Assert.Null(retry.RetryAtUtc);
        Assert.Equal(retryAt.AddMinutes(1), retry.RecordFailure(retryAt));
    }

    [Fact]
    public async Task SelectionCooldownSurvivesCloseAndNavigationWithoutSuppressingHealthyCity()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        var navigation = new StatsSnapshotStoreTests.NavigationSpy();
        var store = new StatsSnapshotStore(endpoints, clock, new StatsSnapshotStoreTests.VisibilitySpy(), navigation,
            statsEnabled: true, preparationEnabled: false);
        var toronto = new StatsSelection("city", "toronto", []);
        var montreal = new StatsSelection("city", "montreal", []);
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(toronto);
            var firstAttempt = store.EnsureRequestedAsync(toronto);
            var failedCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            failedCall.Return(Failure(HttpStatusCode.ServiceUnavailable, "read_timeout"));
            await firstAttempt.WaitAsync(TimeSpan.FromSeconds(2));

            store.SetOverlaySelection(null);
            navigation.NavigateTo("montreal");
            var healthyAttempt = store.EnsureRequestedAsync(montreal);
            var healthyCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            healthyCall.Return(Success(clock, montreal));
            await healthyAttempt.WaitAsync(TimeSpan.FromSeconds(2));

            navigation.NavigateTo("toronto");
            store.SetOverlaySelection(toronto);
            var immediateRetry = store.EnsureRequestedAsync(toronto);
            Assert.Equal(2, endpoints.Calls.Count);
            Assert.Equal("read_timeout", store.TryGet(toronto)?.ProblemCode);
            await immediateRetry.WaitAsync(TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(1));
            await store.EnsureRequestedAsync(toronto).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, endpoints.Calls.Count);
            clock.Advance(TimeSpan.FromTicks(1));

            var eligibleAttempt = store.EnsureRequestedAsync(toronto);
            var retryCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            retryCall.Return(Success(clock, toronto));
            await eligibleAttempt.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(3, endpoints.Calls.Count);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task RateLimitCooldownAppliesAcrossSelectionsAndOpensAtExactRetryAfter()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false);
        var city = new StatsSelection("city", "toronto", []);
        var all = new StatsSelection("all", null, []);
        try
        {
            await store.StartAsync("toronto");
            var rateLimited = store.EnsureRequestedAsync(city);
            var limitedCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            limitedCall.Return(Failure(HttpStatusCode.TooManyRequests, "rate_limited", TimeSpan.FromMinutes(5)));
            await rateLimited.WaitAsync(TimeSpan.FromSeconds(2));

            var earlyAll = store.EnsureRequestedAsync(all);
            Assert.Single(endpoints.Calls);
            Assert.Equal("rate_limited", store.TryGet(all)?.ProblemCode);
            await earlyAll.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
            var almostEligible = store.EnsureRequestedAsync(all);
            Assert.Single(endpoints.Calls);
            await almostEligible.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromTicks(1));

            var healthyAfterCooldown = store.EnsureRequestedAsync(all);
            var allCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("all", allCall.Request.Scope);
            allCall.Return(Success(clock, all));
            await healthyAfterCooldown.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, endpoints.Calls.Count);
        }
        finally { await store.StopAsync(); }
    }

    static HttpServiceResponse<StatsOverlayResponse> Failure(
        HttpStatusCode statusCode, string problemCode, TimeSpan? retryAfter = null) => new(
            Result<StatsOverlayResponse>.Error(problemCode), statusCode, problemCode, retryAfter);

    static HttpServiceResponse<StatsOverlayResponse> Success(
        StatsSnapshotTestTimeProvider clock, StatsSelection selection)
    {
        var generated = clock.GetUtcNow().UtcDateTime;
        var response = StatsSnapshotAcceptanceTests.ValidResponse(selection, generated);
        return new HttpServiceResponse<StatsOverlayResponse>(
            Result<StatsOverlayResponse>.Success(response), HttpStatusCode.OK, null, null);
    }
}
