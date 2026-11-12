using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperienceWindowViewTests
{
    [ClientExperiencePostgresFact]
    public async Task OverlappingWindowsAreQuarantinedAndValidPartitionsRemainDistinct()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        await using var context = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var pageId = Guid.NewGuid();
        var activationId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var overlapId = Guid.NewGuid();
        var validId = Guid.NewGuid();
        var duplicateOneId = Guid.NewGuid();
        var duplicateTwoId = Guid.NewGuid();
        context.ClientExperienceEvents.AddRange(
            Window(firstId, pageId, activationId, 1, 0, 60_000, 60_000, 0, 0, now),
            Window(overlapId, pageId, activationId, 2, 30_000, 90_000, 60_000, 0, 0, now),
            Window(validId, pageId, activationId, 3, 90_000, 150_000, 30_000, 10_000, 20_000, now),
            Window(duplicateOneId, pageId, activationId, 4, 150_000, 210_000, 60_000, 0, 0, now),
            Window(duplicateTwoId, pageId, activationId, 4, 150_000, 210_000, 60_000, 0, 0, now));
        await context.SaveChangesAsync();
        await context.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        Assert.Equal(0L, await CountAsync(connection,
            "SELECT COUNT(*) FROM client_experience_validated_windows WHERE event_id = ANY(@ids)", [firstId, overlapId]));
        Assert.Equal(1L, await CountAsync(connection,
            "SELECT COUNT(*) FROM client_experience_validated_windows WHERE event_id = @id", validId));
        Assert.Equal(4L, await CountAsync(connection,
            "SELECT COUNT(*) FROM client_experience_window_states WHERE event_id = @id", validId));
        Assert.Equal(0L, await CountAsync(connection,
            "SELECT COUNT(*) FROM client_experience_validated_windows WHERE event_id = ANY(@ids)", [duplicateOneId, duplicateTwoId]));
        Assert.True(await CountAsync(connection,
            "SELECT COUNT(*) FROM client_experience_window_quality WHERE page_session_id = @id AND quality_code IN ('overlapping_window','logical_window_conflict')", pageId) >= 1);
    }

    static async Task<long> CountAsync(NpgsqlConnection connection, string sql, Guid id)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    static async Task<long> CountAsync(NpgsqlConnection connection, string sql, Guid[] ids)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", ids);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    static ClientExperienceEvent Window(Guid eventId, Guid pageId, Guid activationId, long sequence, long start, long end,
        long observed, long gap, long excluded, DateTimeOffset received)
    {
        var payload = JsonSerializer.Serialize(new
        {
            windowSequence = sequence,
            windowStartMs = start,
            windowEndMs = end,
            observedMs = observed,
            gapMs = gap,
            excludedMs = excluded,
            stateDurations = new[]
            {
                new { capability = "routes", state = "healthy", durationMs = observed },
                new { capability = "map", state = "unknown", durationMs = observed },
                new { capability = "live_delivery", state = "pending", durationMs = observed },
                new { capability = "audio", state = "degraded", durationMs = observed }
            }
        });
        return new ClientExperienceEvent
        {
            EventId = eventId,
            PageSessionId = pageId,
            CityActivationId = activationId,
            Sequence = sequence,
            OccurredAtUtc = received,
            ReceivedAtUtc = received,
            Environment = "test",
            CitySlug = "atlanta",
            ClientRelease = "test",
            SchemaVersion = 1,
            MeasurementVersion = 1,
            EventKind = "observation_window",
            PageElapsedMs = end,
            Payload = payload
        };
    }
}
