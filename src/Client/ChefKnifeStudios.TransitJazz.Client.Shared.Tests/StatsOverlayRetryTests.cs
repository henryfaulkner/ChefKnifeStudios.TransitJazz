using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
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
    public void ANewOverlaySessionStartsWithFreshRetryPolicyAfterPriorFailure()
    {
        var retry = new StatsOverlayRetryPolicy();
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        retry.RecordFailure(now);
        Assert.True(retry.IsCoolingDown(now.AddSeconds(1)));

        retry.Reset();

        Assert.Null(retry.RetryAtUtc);
        Assert.Equal(now.AddDays(1).AddSeconds(60), retry.RecordFailure(now.AddDays(1)));
    }
}
