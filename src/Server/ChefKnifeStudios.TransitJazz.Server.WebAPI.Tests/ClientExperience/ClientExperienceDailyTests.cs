using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.ClientExperience;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using ClientExperienceContract = ChefKnifeStudios.TransitJazz.Shared.ClientExperience.ClientExperienceEvent;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperienceDailyTests
{
    [ClientExperiencePostgresFact]
    public async Task LateAttemptResultCorrectsTheStartDayWithoutInflatingCounts()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync();
        Assert.NotNull(fixture);
        var now = new DateTimeOffset(2026, 11, 8, 16, 0, 0, TimeSpan.Zero);
        var attemptId = Guid.NewGuid();
        var secondAttemptId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var cityActivationId = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext())
        {
            db.ClientExperienceEvents.Add(Event(now.AddMinutes(-4), pageId, cityActivationId, attemptId, "attempt_started", new ClientExperienceContract
            {
                SchemaVersion = 1, MeasurementVersion = 1, EventKind = "attempt_started", EventId = Guid.NewGuid(),
                PageSessionId = pageId, Sequence = 1, OccurredAtUtc = now.AddMinutes(-4), PageElapsedMs = 0,
                CitySlug = "atlanta", ClientRelease = "test", AttemptId = attemptId, Journey = "city_map_start",
                Eligible = true, DeadlineForegroundMs = 60_000
            }));
            db.ClientExperienceEvents.Add(Event(now.AddMinutes(-3), pageId, cityActivationId, secondAttemptId, "attempt_started", new ClientExperienceContract
            {
                SchemaVersion = 1, MeasurementVersion = 1, EventKind = "attempt_started", EventId = Guid.NewGuid(),
                PageSessionId = pageId, Sequence = 2, OccurredAtUtc = now.AddMinutes(-3), PageElapsedMs = 60_000,
                CitySlug = "atlanta", ClientRelease = "test", AttemptId = secondAttemptId, Journey = "city_map_start",
                Eligible = true, DeadlineForegroundMs = 60_000
            }));
            await db.SaveChangesAsync();
        }

        var aggregator = new ClientExperienceDailyAggregator(fixture, new FixedTimeProvider(now));
        Assert.True(await aggregator.RunOnceAsync());
        await using (var db = fixture.CreateDbContext())
        {
            var provisional = await db.Set<ClientExperienceDaily>().SingleAsync(row => row.SeriesKind == "journey" && row.SeriesCode == "city_map_start");
            Assert.Equal(2, provisional.Unresolved);
            Assert.Equal(0, provisional.Succeeded);
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.ClientExperienceEvents.Add(Event(now, pageId, cityActivationId, attemptId, "attempt_finished", new ClientExperienceContract
            {
                SchemaVersion = 1, MeasurementVersion = 1, EventKind = "attempt_finished", EventId = Guid.NewGuid(),
                PageSessionId = pageId, Sequence = 2, OccurredAtUtc = now, PageElapsedMs = 240_000,
                CitySlug = "atlanta", ClientRelease = "test", AttemptId = attemptId, Journey = "city_map_start",
                Outcome = "succeeded", Reason = "owner_confirmed", ForegroundElapsedMs = 2_000, WallElapsedMs = 4_000
            }));
            db.ClientExperienceEvents.Add(Event(now.AddSeconds(1), pageId, cityActivationId, secondAttemptId, "attempt_finished", new ClientExperienceContract
            {
                SchemaVersion = 1, MeasurementVersion = 1, EventKind = "attempt_finished", EventId = Guid.NewGuid(),
                PageSessionId = pageId, Sequence = 3, OccurredAtUtc = now.AddSeconds(1), PageElapsedMs = 240_001,
                CitySlug = "atlanta", ClientRelease = "test", AttemptId = secondAttemptId, Journey = "city_map_start",
                Outcome = "succeeded", Reason = "owner_confirmed", ForegroundElapsedMs = 6_000, WallElapsedMs = 8_000
            }));
            await db.SaveChangesAsync();
        }

        Assert.True(await aggregator.RunOnceAsync());
        await using (var db = fixture.CreateDbContext())
        {
            var corrected = await db.Set<ClientExperienceDaily>().SingleAsync(row => row.SeriesKind == "journey" && row.SeriesCode == "city_map_start");
            Assert.Equal(2, corrected.EligibleStarts);
            Assert.Equal(2, corrected.Succeeded);
            Assert.Equal(0, corrected.Unresolved);
            Assert.Equal(1, corrected.LatencyLe5000Ms);
            Assert.Equal(1, corrected.LatencyLe15000Ms);
        }
    }

    [ClientExperiencePostgresFact]
    public async Task WatermarkCatchupReplacesAffectedBucketAndRepeatedRunsAreIdempotent()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync();
        Assert.NotNull(fixture);
        var now = new DateTimeOffset(2026, 11, 8, 16, 0, 0, TimeSpan.Zero);
        var receivedAt = now.AddDays(-13);
        var pageId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var cityActivationId = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext())
        {
            db.ClientExperienceMaintenance.Add(new ClientExperienceMaintenance
            {
                Id = 1,
                WatermarkUtc = now.AddDays(-15),
                LastSuccessUtc = now.AddDays(-15),
                JobVersion = 1
            });
            db.ClientExperienceDaily.Add(new ClientExperienceDaily
            {
                DayUtc = DateOnly.FromDateTime(receivedAt.UtcDateTime), Environment = "test", CitySlug = "atlanta",
                ClientRelease = "test", MeasurementVersion = 1, TimeBasis = "receipt", SeriesKind = "journey",
                SeriesCode = "city_map_start", EligibleStarts = 99, Succeeded = 99, EventCount = 99
            });
            db.ClientExperienceEvents.Add(Event(receivedAt, pageId, cityActivationId, attemptId, "attempt_started", new ClientExperienceContract
            {
                SchemaVersion = 1, MeasurementVersion = 1, EventKind = "attempt_started", EventId = Guid.NewGuid(),
                PageSessionId = pageId, Sequence = 1, OccurredAtUtc = receivedAt, PageElapsedMs = 0,
                CitySlug = "atlanta", ClientRelease = "test", AttemptId = attemptId, Journey = "city_map_start",
                Eligible = true, DeadlineForegroundMs = 60_000
            }));
            await db.SaveChangesAsync();
        }

        var aggregator = new ClientExperienceDailyAggregator(fixture, new FixedTimeProvider(now));
        Assert.True(await aggregator.RunOnceAsync());
        await AssertUnresolvedBucket(fixture, receivedAt, expected: 1);
        Assert.True(await aggregator.RunOnceAsync());
        await AssertUnresolvedBucket(fixture, receivedAt, expected: 1);
    }

    [ClientExperiencePostgresFact]
    public async Task FailedAggregateTransactionDoesNotDeleteExpiredRawEvents()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync();
        Assert.NotNull(fixture);
        var now = new DateTimeOffset(2026, 11, 8, 16, 0, 0, TimeSpan.Zero);
        var oldEvent = new ChefKnifeStudios.TransitJazz.Server.Data.Models.ClientExperienceEvent
        {
            EventId = Guid.NewGuid(), PageSessionId = Guid.NewGuid(), CityActivationId = Guid.NewGuid(), Sequence = 1,
            OccurredAtUtc = now.AddDays(-31), ReceivedAtUtc = now.AddDays(-31), Environment = "test", CitySlug = "atlanta",
            ClientRelease = "test", SchemaVersion = 1, MeasurementVersion = 1, EventKind = "observation_window",
            Payload = "{\"windowSequence\":\"not-an-integer\"}"
        };
        await using (var db = fixture.CreateDbContext())
        {
            db.ClientExperienceEvents.Add(oldEvent);
            await db.SaveChangesAsync();
        }

        var aggregator = new ClientExperienceDailyAggregator(fixture, new FixedTimeProvider(now));
        await Assert.ThrowsAsync<PostgresException>(() => aggregator.RunOnceAsync());
        await using var verify = fixture.CreateDbContext();
        Assert.True(await verify.ClientExperienceEvents.AnyAsync(row => row.EventId == oldEvent.EventId));
    }

    static async Task AssertUnresolvedBucket(ClientExperienceDatabaseFixture fixture, DateTimeOffset receivedAt, long expected)
    {
        await using var db = fixture.CreateDbContext();
        var row = await db.Set<ClientExperienceDaily>().SingleAsync(value => value.DayUtc == DateOnly.FromDateTime(receivedAt.UtcDateTime)
            && value.SeriesKind == "journey" && value.SeriesCode == "city_map_start");
        Assert.Equal(expected, row.EligibleStarts);
        Assert.Equal(expected, row.Unresolved);
        Assert.Equal(0, row.Succeeded);
    }

    static ChefKnifeStudios.TransitJazz.Server.Data.Models.ClientExperienceEvent Event(DateTimeOffset receivedAt, Guid pageId, Guid cityActivationId, Guid attemptId, string kind, ClientExperienceContract observation)
        => new()
        {
            EventId = observation.EventId,
            PageSessionId = pageId,
            CityActivationId = cityActivationId,
            Sequence = observation.Sequence,
            OccurredAtUtc = observation.OccurredAtUtc,
            ReceivedAtUtc = receivedAt,
            Environment = "test",
            CitySlug = "atlanta",
            ClientRelease = "test",
            SchemaVersion = 1,
            MeasurementVersion = 1,
            EventKind = kind,
            PageElapsedMs = observation.PageElapsedMs,
            AttemptId = attemptId,
            Journey = observation.Journey,
            Outcome = observation.Outcome,
            Payload = JsonSerializer.Serialize(observation, JsonSettings.DefaultOptions)
        };

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
