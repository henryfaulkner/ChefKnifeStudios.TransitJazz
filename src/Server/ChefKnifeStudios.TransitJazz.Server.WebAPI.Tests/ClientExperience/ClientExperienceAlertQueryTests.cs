using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperienceAlertQueryTests
{
    [Fact]
    public void AlertPackHasGuardedRulesAndUsesTheReviewedSqlFiles()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "alerts", "transitjazz-client-experience-alerts.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("apiVersion").GetInt32());
        var rules = root.GetProperty("groups").EnumerateArray()
            .SelectMany(group => group.GetProperty("rules").EnumerateArray().ToArray())
            .ToArray();
        var expectedRuleUids = new[]
        {
            "ce_journey_failure_high", "ce_journey_coverage_advisory", "ce_capability_degradation_high",
            "ce_capability_coverage_advisory", "ce_audio_stop_still_playing", "ce_trend_maintenance_stalled",
            "ce_toast_volume_advisory", "ce_ingestion_persistence_failures", "ce_database_probe_unavailable",
            "ce_database_probe_missing", "ce_maintenance_repeated_failures",
            "ce_synthetic_app_readiness_failure", "ce_synthetic_runner_failure"
        };
        foreach (var uid in expectedRuleUids)
            Assert.Contains(rules, rule => rule.GetProperty("uid").GetString() == uid);
        Assert.Equal(expectedRuleUids.Length, rules.Length);

        var sqlRules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ce_journey_failure_high"] = "journey-failure.sql",
            ["ce_journey_coverage_advisory"] = "journey-coverage.sql",
            ["ce_capability_degradation_high"] = "capability-degradation.sql",
            ["ce_capability_coverage_advisory"] = "capability-coverage.sql",
            ["ce_audio_stop_still_playing"] = "audio-stop-still-playing.sql",
            ["ce_trend_maintenance_stalled"] = "maintenance-stalled.sql",
            ["ce_toast_volume_advisory"] = "toast-volume-advisory.sql"
        };

        foreach (var rule in rules)
        {
            Assert.Equal("Alerting", rule.GetProperty("noDataState").GetString());
            Assert.Equal("Alerting", rule.GetProperty("execErrState").GetString());
            Assert.Equal(6, rule.GetProperty("missing_series_evals_to_resolve").GetInt32());
            var data = rule.GetProperty("data").EnumerateArray().ToArray();
            Assert.NotEmpty(data);
            if (!sqlRules.TryGetValue(rule.GetProperty("uid").GetString()!, out var sqlFile)) continue;
            var query = data.Select(item => item.GetProperty("model"))
                .First(model => model.TryGetProperty("rawSql", out _))
                .GetProperty("rawSql").GetString();
            var reviewedSql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "alerts", "sql", sqlFile));
            Assert.Equal(NormalizeSql(reviewedSql), NormalizeSql(query!));
            Assert.Contains("ORDER BY", query!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AS \"time\"", query!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AS value", query!, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(sqlRules.Count, rules.Count(rule => sqlRules.ContainsKey(rule.GetProperty("uid").GetString()!)));
    }

    [ClientExperiencePostgresFact]
    public async Task JourneyAlertRequiresTrafficAndFreshCoverageAndKeepsValidZeroAndConfiguredCities()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            AddAttempts(db, now, "atlanta", 20, failed: 3, sessions: 3);
            AddAttempts(db, now, "boston", 20, failed: 0, sessions: 3);
            AddAttempts(db, now, "toronto", 20, failed: 2, sessions: 3);
            AddAttempts(db, now, "washington-dc", 20, failed: 1, sessions: 3);
            AddAttempts(db, now, "denver", 19, failed: 19, sessions: 3);
            AddAttempts(db, now, "philadelphia", 20, failed: 0, sessions: 3, staleResults: 3);
            AddAttempts(db, now, "seattle", 20, failed: 20, sessions: 2);
            await db.SaveChangesAsync();
        }

        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var results = await ReadSeriesAsync((NpgsqlConnection)context.Database.GetDbConnection(), "journey-failure.sql");
        Assert.Equal(1d, results[("Production", "atlanta", "city_map_start")]);
        Assert.Equal(0d, results[("Production", "boston", "city_map_start")]!.Value, 4);
        Assert.Equal(1d, results[("Production", "toronto", "city_map_start")]);
        Assert.Equal(0d, results[("Production", "washington-dc", "city_map_start")]);
        Assert.Null(results[("Production", "denver", "city_map_start")]);
        Assert.Null(results[("Production", "philadelphia", "city_map_start")]);
        Assert.Null(results[("Production", "seattle", "city_map_start")]);
        Assert.Null(results[("Production", "unknown", "city_map_start")]);
    }

    [ClientExperiencePostgresFact]
    public async Task JourneyCoverageQueryRaisesAdvisoryForHighUnresolvedShareAndReturnsConfiguredZeroSeries()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            AddAttempts(db, now, "atlanta", 20, failed: 0, sessions: 3, unresolved: 3);
            AddAttempts(db, now, "boston", 20, failed: 0, sessions: 3);
            await db.SaveChangesAsync();
        }

        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var results = await ReadSeriesAsync((NpgsqlConnection)context.Database.GetDbConnection(), "journey-coverage.sql");
        Assert.Equal(1d, results[("Production", "atlanta", "city_map_start")]);
        Assert.Equal(0d, results[("Production", "boston", "city_map_start")]);
        Assert.Equal(0d, results[("Production", "unknown", "city_map_start")]);
        Assert.Equal(90, results.Count);
    }

    [ClientExperiencePostgresFact]
    public async Task CapabilityAlertSuppressesHighDegradationWhenGapCoverageIsTooLarge()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            AddObservationWindows(db, now, "atlanta", gap: 0, audioDegraded: 6_000);
            AddObservationWindows(db, now, "boston", gap: 12_000, audioDegraded: 12_000);
            await db.SaveChangesAsync();
        }

        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var degradation = await ReadSeriesAsync(connection, "capability-degradation.sql");
        var coverage = await ReadSeriesAsync(connection, "capability-coverage.sql");
        Assert.Equal(1d, degradation[("Production", "atlanta", "audio")]!.Value);
        Assert.Null(degradation[("Production", "boston", "audio")]);
        Assert.Equal(1d, coverage[("Production", "boston", "audio")]);
        Assert.Equal(0d, coverage[("Production", "atlanta", "audio")]);
    }

    [ClientExperiencePostgresFact]
    public async Task FreshStopFailureFiresAtLowTrafficButDelayedBacklogDoesNot()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            AddStopAttempt(db, now, "atlanta", delayed: false);
            AddStopAttempt(db, now, "boston", delayed: true);
            await db.SaveChangesAsync();
        }

        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var results = await ReadSeriesAsync((NpgsqlConnection)context.Database.GetDbConnection(), "audio-stop-still-playing.sql");
        Assert.Equal(1d, results[("Production", "atlanta", "still_playing")]);
        Assert.Equal(0d, results[("Production", "boston", "still_playing")]);
        Assert.Equal(0d, results[("Production", "unknown", "still_playing")]);
    }

    [ClientExperiencePostgresFact]
    public async Task MaintenanceAlertDistinguishesFreshWatermarkFromStalledAndMissingState()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        Assert.Equal(1d, await ReadSingleValueAsync(connection, "maintenance-stalled.sql"));

        var now = DateTimeOffset.UtcNow;
        context.ClientExperienceMaintenance.Add(new ClientExperienceMaintenance
        {
            Id = 1, WatermarkUtc = now.AddHours(-1), LastSuccessUtc = now.AddHours(-1),
            LastFailureUtc = now.AddMinutes(-1), LastFailureCode = "transient_failure", JobVersion = 1
        });
        await context.SaveChangesAsync();
        Assert.Equal(0d, await ReadSingleValueAsync(connection, "maintenance-stalled.sql"));

        var row = await context.ClientExperienceMaintenance.SingleAsync(item => item.Id == 1);
        row.WatermarkUtc = now.AddHours(-3);
        await context.SaveChangesAsync();
        Assert.Equal(1d, await ReadSingleValueAsync(connection, "maintenance-stalled.sql"));
    }

    static void AddAttempts(AppDbContext db, DateTimeOffset now, string city, int count, int failed, int sessions,
        int staleResults = 0, int unresolved = 0)
    {
        var pageIds = Enumerable.Range(0, sessions).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < count; index++)
        {
            var pageId = pageIds[index % pageIds.Length];
            var activationId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            var startTime = now.AddMinutes(-5);
            AddEvent(db, new ClientExperienceEvent
            {
                EventId = Guid.NewGuid(), PageSessionId = pageId, CityActivationId = activationId, AttemptId = attemptId,
                Sequence = index + 1, OccurredAtUtc = startTime, ReceivedAtUtc = startTime, Environment = "Production",
                CitySlug = city, ClientRelease = "alert-test", SchemaVersion = 1, MeasurementVersion = 1,
                EventKind = "attempt_started", Journey = "city_map_start", PageElapsedMs = index * 100,
                Payload = JsonSerializer.Serialize(new { attemptId, journey = "city_map_start", eligible = true })
            });

            if (index < unresolved) continue;
            var outcome = index < failed ? "failed" : "succeeded";
            var resultTime = now.AddMinutes(-5);
            var occurredAt = index < staleResults ? resultTime.AddMinutes(-5) : resultTime;
            AddEvent(db, new ClientExperienceEvent
            {
                EventId = Guid.NewGuid(), PageSessionId = pageId, CityActivationId = activationId, AttemptId = attemptId,
                Sequence = index + 1, OccurredAtUtc = occurredAt, ReceivedAtUtc = resultTime, Environment = "Production",
                CitySlug = city, ClientRelease = "alert-test", SchemaVersion = 1, MeasurementVersion = 1,
                EventKind = "attempt_finished", Journey = "city_map_start", Outcome = outcome, PageElapsedMs = index * 100 + 10,
                Payload = JsonSerializer.Serialize(new
                {
                    attemptId, journey = "city_map_start", outcome,
                    reason = outcome == "succeeded" ? "owner_confirmed" : "owner_failed",
                    foregroundElapsedMs = 10, wallElapsedMs = 10
                })
            });
        }
    }

    static void AddObservationWindows(AppDbContext db, DateTimeOffset now, string city, long gap, long audioDegraded)
    {
        var pageStart = now.AddMinutes(-11);
        for (var session = 0; session < 3; session++)
        {
            var pageId = Guid.NewGuid();
            var activationId = Guid.NewGuid();
            for (var index = 1; index <= 10; index++)
            {
                var endMs = index * 60_000L;
                var observed = 60_000L - gap;
                var occurredAt = pageStart.AddMilliseconds(endMs);
                var audioHealthy = observed - audioDegraded;
                var stateDurations = new[]
                {
                    new { capability = "routes", state = "healthy", durationMs = observed },
                    new { capability = "map", state = "healthy", durationMs = observed },
                    new { capability = "live_delivery", state = "healthy", durationMs = observed },
                    new { capability = "audio", state = "healthy", durationMs = audioHealthy },
                    new { capability = "audio", state = "degraded", durationMs = audioDegraded }
                };
                AddEvent(db, new ClientExperienceEvent
                {
                    EventId = Guid.NewGuid(), PageSessionId = pageId, CityActivationId = activationId,
                    Sequence = index, OccurredAtUtc = occurredAt, ReceivedAtUtc = occurredAt, Environment = "Production",
                    CitySlug = city, ClientRelease = "alert-test", SchemaVersion = 1, MeasurementVersion = 1,
                    EventKind = "observation_window", PageElapsedMs = endMs,
                    Payload = JsonSerializer.Serialize(new
                    {
                        windowSequence = index, windowStartMs = endMs - 60_000, windowEndMs = endMs,
                        observedMs = observed, gapMs = gap, excludedMs = 0, stateDurations
                    })
                });
            }
        }
    }

    static void AddStopAttempt(AppDbContext db, DateTimeOffset now, string city, bool delayed)
    {
        var pageId = Guid.NewGuid();
        var activationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var receivedAt = now.AddSeconds(-30);
        var occurredAt = delayed ? receivedAt.AddMinutes(-5) : receivedAt;
        AddEvent(db, new ClientExperienceEvent
        {
            EventId = Guid.NewGuid(), PageSessionId = pageId, CityActivationId = activationId, AttemptId = attemptId,
            Sequence = 1, OccurredAtUtc = occurredAt, ReceivedAtUtc = receivedAt, Environment = "Production",
            CitySlug = city, ClientRelease = "alert-test", SchemaVersion = 1, MeasurementVersion = 1,
            EventKind = "attempt_started", Journey = "audio_stop", PageElapsedMs = 0,
            Payload = JsonSerializer.Serialize(new { attemptId, journey = "audio_stop", action = "audio_stop", eligible = true })
        });
        AddEvent(db, new ClientExperienceEvent
        {
            EventId = Guid.NewGuid(), PageSessionId = pageId, CityActivationId = activationId, AttemptId = attemptId,
            Sequence = 2, OccurredAtUtc = occurredAt, ReceivedAtUtc = receivedAt, Environment = "Production",
            CitySlug = city, ClientRelease = "alert-test", SchemaVersion = 1, MeasurementVersion = 1,
            EventKind = "attempt_finished", Journey = "audio_stop", Outcome = "failed", PageElapsedMs = 1,
            Payload = JsonSerializer.Serialize(new { attemptId, journey = "audio_stop", action = "audio_stop", outcome = "failed", reason = "still_playing" })
        });
    }

    static void AddEvent(AppDbContext db, ClientExperienceEvent item) => db.ClientExperienceEvents.Add(item);

    static async Task<Dictionary<(string Environment, string City, string Series), double?>> ReadSeriesAsync(
        NpgsqlConnection connection, string fileName)
    {
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(QueryPath(fileName)), connection);
        await using var reader = await command.ExecuteReaderAsync();
        var results = new Dictionary<(string, string, string), double?>();
        while (await reader.ReadAsync())
        {
            var key = (reader.GetString(1), reader.GetString(2), reader.GetString(3));
            results.Add(key, await reader.IsDBNullAsync(4) ? null : reader.GetDouble(4));
        }
        return results;
    }

    static async Task<double> ReadSingleValueAsync(NpgsqlConnection connection, string fileName)
    {
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(QueryPath(fileName)), connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return reader.GetDouble(2);
    }

    static string QueryPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "alerts", "sql", fileName);

    static string NormalizeSql(string value) => string.Join('\n', value.Replace("\r", "").Split('\n').Select(line => line.TrimEnd())).Trim();
}
