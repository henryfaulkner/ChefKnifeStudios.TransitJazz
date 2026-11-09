using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ChefKnifeStudios.TransitJazz.Server.Data.ClientExperience;

public sealed record ClientExperienceAppendResult(IReadOnlyList<Guid> AcknowledgedIds, int Inserted, int Duplicates);
public sealed record ClientExperienceStoredEvent(ClientExperienceEvent Event, bool ClockSkewed);

public interface IClientExperienceEventStore
{
    Task<ClientExperienceAppendResult> AppendAsync(
        IReadOnlyList<ClientExperienceStoredEvent> events,
        string environment,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken);
}

public sealed class ClientExperienceEventStore(IDbContextFactory<AppDbContext> contextFactory) : IClientExperienceEventStore
{
    public async Task<ClientExperienceAppendResult> AppendAsync(
        IReadOnlyList<ClientExperienceStoredEvent> events,
        string environment,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acknowledged = new List<Guid>(events.Count);
        var inserted = 0;
        var duplicates = 0;

        foreach (var item in events)
        {
            var e = item.Event;
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO client_experience_events
                    (event_id, page_session_id, city_activation_id, sequence, occurred_at_utc, received_at_utc,
                     clock_skewed, environment, city_slug, client_release, schema_version, measurement_version,
                     event_kind, page_elapsed_ms, attempt_id, episode_id, journey, capability, outcome, payload)
                VALUES
                    ({e.EventId}, {e.PageSessionId}, {e.CityActivationId}, {e.Sequence}, {e.OccurredAtUtc}, {receivedAtUtc},
                     {item.ClockSkewed}, {environment}, {e.CitySlug}, {e.ClientRelease}, {e.SchemaVersion}, {e.MeasurementVersion},
                     {e.EventKind}, {e.PageElapsedMs}, {e.AttemptId}, {e.EpisodeId}, {e.Journey}, {e.Capability}, {e.Outcome}, CAST({e.Payload} AS jsonb))
                ON CONFLICT (event_id) DO NOTHING
                """, cancellationToken);

            acknowledged.Add(e.EventId);
            if (affected == 1) inserted++;
            else duplicates++;
        }

        await transaction.CommitAsync(cancellationToken);
        return new(acknowledged.Distinct().ToArray(), inserted, duplicates);
    }
}
