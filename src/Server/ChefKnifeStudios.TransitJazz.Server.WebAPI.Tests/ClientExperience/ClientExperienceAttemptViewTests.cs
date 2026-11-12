using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperienceAttemptViewTests
{
    [ClientExperiencePostgresFact]
    public async Task ResultBeforeStartReconcilesAndConflictingTerminalResultsAreQuarantined()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        await using var context = fixture.CreateDbContext();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var now = DateTimeOffset.UtcNow;
        var pageId = Guid.NewGuid();
        var activationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        context.ClientExperienceEvents.Add(Finished(Guid.NewGuid(), pageId, activationId, attemptId, now, "succeeded"));
        await context.SaveChangesAsync();
        await context.Database.OpenConnectionAsync();

        Assert.Equal(1L, await CountAsync(connection,
            "SELECT COUNT(*) FROM client_experience_attempt_quality WHERE attempt_id = @attemptId AND quality_code = 'unmatched_result'", attemptId));

        context.ClientExperienceEvents.Add(Started(Guid.NewGuid(), pageId, activationId, attemptId, now.AddSeconds(-3)));
        await context.SaveChangesAsync();
        await using (var reconciled = new NpgsqlCommand(
            "SELECT outcome, logical_conflict, foreground_elapsed_ms FROM client_experience_attempts WHERE attempt_id = @attemptId", connection))
        {
            reconciled.Parameters.AddWithValue("attemptId", attemptId);
            await using var reader = await reconciled.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("succeeded", reader.GetString(0));
            Assert.False(reader.GetBoolean(1));
            Assert.Equal(2_500L, reader.GetInt64(2));
        }
        Assert.Equal(0L, await CountAsync(connection,
            "SELECT COUNT(*) FROM client_experience_attempt_quality WHERE attempt_id = @attemptId", attemptId));

        context.ClientExperienceEvents.Add(Finished(Guid.NewGuid(), pageId, activationId, attemptId, now.AddSeconds(1), "failed"));
        await context.SaveChangesAsync();
        await using var conflicted = new NpgsqlCommand(
            "SELECT outcome, logical_conflict FROM client_experience_attempts WHERE attempt_id = @attemptId", connection);
        conflicted.Parameters.AddWithValue("attemptId", attemptId);
        await using var conflictedReader = await conflicted.ExecuteReaderAsync();
        Assert.True(await conflictedReader.ReadAsync());
        Assert.Equal("conflict", conflictedReader.GetString(0));
        Assert.True(conflictedReader.GetBoolean(1));
    }

    static async Task<long> CountAsync(NpgsqlConnection connection, string sql, Guid attemptId)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("attemptId", attemptId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    static ClientExperienceEvent Started(Guid eventId, Guid pageId, Guid activationId, Guid attemptId, DateTimeOffset received)
        => new()
        {
            EventId = eventId,
            PageSessionId = pageId,
            CityActivationId = activationId,
            Sequence = 1,
            OccurredAtUtc = received,
            ReceivedAtUtc = received,
            Environment = "test",
            CitySlug = "atlanta",
            ClientRelease = "test",
            SchemaVersion = 1,
            MeasurementVersion = 1,
            EventKind = "attempt_started",
            PageElapsedMs = 100,
            AttemptId = attemptId,
            Journey = "city_map_start",
            Payload = JsonSerializer.Serialize(new { attemptId, journey = "city_map_start", eligible = true })
        };

    static ClientExperienceEvent Finished(Guid eventId, Guid pageId, Guid activationId, Guid attemptId,
        DateTimeOffset received, string outcome)
        => new()
        {
            EventId = eventId,
            PageSessionId = pageId,
            CityActivationId = activationId,
            Sequence = 2,
            OccurredAtUtc = received,
            ReceivedAtUtc = received,
            Environment = "test",
            CitySlug = "atlanta",
            ClientRelease = "test",
            SchemaVersion = 1,
            MeasurementVersion = 1,
            EventKind = "attempt_finished",
            PageElapsedMs = 3_000,
            AttemptId = attemptId,
            Outcome = outcome,
            Payload = JsonSerializer.Serialize(new
            {
                attemptId,
                journey = "city_map_start",
                outcome,
                reason = outcome == "succeeded" ? "owner_confirmed" : "owner_failed",
                foregroundElapsedMs = 2_500,
                wallElapsedMs = 3_000
            })
        };
}
