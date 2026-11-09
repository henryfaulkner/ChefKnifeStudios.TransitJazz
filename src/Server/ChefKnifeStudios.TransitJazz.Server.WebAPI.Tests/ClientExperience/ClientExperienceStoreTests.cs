using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.ClientExperience;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperienceStoreTests
{
    [ClientExperiencePostgresFact]
    public async Task ConcurrentSameUuidIsStoredOnceAndAcknowledgedByBothCallers()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        var store = new ClientExperienceEventStore(fixture);
        var received = DateTimeOffset.UtcNow;
        var item = Stored(Event(Guid.NewGuid()), false);
        var first = store.AppendAsync([item], "test", received, CancellationToken.None);
        var second = store.AppendAsync([item], "test", received.AddSeconds(1), CancellationToken.None);

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal([item.Event.EventId], result.AcknowledgedIds));
        Assert.Equal(1, results.Sum(result => result.Inserted));
        Assert.Equal(1, results.Sum(result => result.Duplicates));
    }

    [ClientExperiencePostgresFact]
    public async Task ReplayPreservesTheFirstReceiptAndEnvironmentAfterLostAcknowledgement()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        var store = new ClientExperienceEventStore(fixture);
        var item = Stored(Event(Guid.NewGuid()), false);
        var firstReceipt = DateTimeOffset.UtcNow;
        _ = await store.AppendAsync([item], "staging", firstReceipt, CancellationToken.None);

        var replay = await store.AppendAsync([item], "production", firstReceipt.AddMinutes(3), CancellationToken.None);

        Assert.Equal(0, replay.Inserted);
        Assert.Equal(1, replay.Duplicates);
        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "SELECT received_at_utc, environment FROM client_experience_events WHERE event_id = @id", connection);
        command.Parameters.AddWithValue("id", item.Event.EventId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(firstReceipt.AddTicks(-(firstReceipt.Ticks % 10)), reader.GetFieldValue<DateTimeOffset>(0));
        Assert.Equal("staging", reader.GetString(1));
    }

    [ClientExperiencePostgresFact]
    public async Task BatchFailureRollsBackEveryInsertAndReturnsNoAcknowledgement()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        var store = new ClientExperienceEventStore(fixture);
        var first = Stored(Event(Guid.NewGuid()), false);
        var broken = Stored(Event(Guid.NewGuid(), "{broken"), false);

        await Assert.ThrowsAnyAsync<Exception>(() => store.AppendAsync(
            [first, broken], "test", DateTimeOffset.UtcNow, CancellationToken.None));

        await using var context = fixture.CreateDbContext();
        Assert.Equal(0, await context.ClientExperienceEvents.CountAsync(x => x.EventId == first.Event.EventId));
    }

    static ClientExperienceStoredEvent Stored(ClientExperienceEvent value, bool skewed)
        => new(value, skewed);

    static ClientExperienceEvent Event(Guid id, string? payload = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ClientExperienceEvent
        {
            SchemaVersion = 1,
            MeasurementVersion = 1,
            EventKind = "session_started",
            EventId = id,
            PageSessionId = Guid.NewGuid(),
            Sequence = 1,
            OccurredAtUtc = now,
            PageElapsedMs = 0,
            CitySlug = "atlanta",
            ClientRelease = "test",
            Payload = payload ?? JsonSerializer.Serialize(new { bootstrapStartOffsetMs = 0 })
        };
    }
}
