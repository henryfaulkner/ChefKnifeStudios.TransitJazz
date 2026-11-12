using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace ChefKnifeStudios.TransitJazz.Server.Data.ClientExperience;

/// <summary>Rebuilds recent receipt-day buckets atomically so late replay corrects, rather than inflates, totals.</summary>
public sealed class ClientExperienceDailyAggregator(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    int rawRetentionDays = 30,
    int dailyRetentionMonths = 13,
    int maximumCleanupRowsPerRun = 50_000)
{
    const int CleanupBatchSize = 1_000;
    const int CorrectionWindowDays = 10;

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acquired = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(63063, 1) AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!acquired) return false;

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var rawCutoff = today.AddDays(-rawRetentionDays);
        var correctionStart = today.AddDays(-CorrectionWindowDays);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO client_experience_maintenance (id, job_version, lag_seconds)
            VALUES (1, 1, 0)
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken);

        var previousWatermark = await db.ClientExperienceMaintenance.AsNoTracking()
            .Where(row => row.Id == 1)
            .Select(row => row.WatermarkUtc)
            .SingleAsync(cancellationToken);
        var oldestRaw = await db.ClientExperienceEvents.AsNoTracking()
            .Select(row => (DateTimeOffset?)row.ReceivedAtUtc)
            .MinAsync(cancellationToken);
        var startDate = previousWatermark is DateTimeOffset watermark
            ? DateOnly.FromDateTime(watermark.UtcDateTime) < correctionStart
                ? DateOnly.FromDateTime(watermark.UtcDateTime).AddDays(-1)
                : correctionStart
            : oldestRaw is DateTimeOffset oldest ? DateOnly.FromDateTime(oldest.UtcDateTime) : correctionStart;

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM client_experience_daily WHERE day_utc >= {startDate}", cancellationToken);
        await RebuildJourneyRowsAsync(db, startDate, cancellationToken);
        await RebuildCapabilityRowsAsync(db, startDate, cancellationToken);
        await RebuildPresentationRowsAsync(db, startDate, cancellationToken);
        await RebuildQualityRowsAsync(db, startDate, cancellationToken);
        await RebuildCollectionRowsAsync(db, startDate, cancellationToken);
        await CleanupDailyAsync(db, now, cancellationToken);
        var deletedRaw = await CleanupRawAsync(db, rawCutoff, cancellationToken);
        var rawBacklog = await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM client_experience_events WHERE received_at_utc < {rawCutoff}::date::timestamp AT TIME ZONE 'UTC') AS \"Value\"")
            .SingleAsync(cancellationToken);
        var lagSeconds = rawBacklog ? rawRetentionDays * 86_400L : 0L;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE client_experience_maintenance
            SET watermark_utc = {now}, last_success_utc = {now}, last_failure_utc = NULL,
                last_failure_code = NULL, lag_seconds = {lagSeconds}, job_version = 1
            WHERE id = 1
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task RecordFailureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO client_experience_maintenance (id, last_failure_utc, last_failure_code, lag_seconds)
                VALUES (1, {now}, 'aggregation_failed', 0)
                ON CONFLICT (id) DO UPDATE SET
                    last_failure_utc = EXCLUDED.last_failure_utc,
                    last_failure_code = EXCLUDED.last_failure_code,
                    lag_seconds = CASE WHEN client_experience_maintenance.last_success_utc IS NULL THEN 86400
                        ELSE GREATEST(0, EXTRACT(EPOCH FROM (EXCLUDED.last_failure_utc - client_experience_maintenance.last_success_utc))::bigint) END
                """, cancellationToken);
        }
        catch { /* Maintenance failure reporting must not terminate the host. */ }
    }

    static async Task RebuildJourneyRowsAsync(AppDbContext db, DateOnly startDate, CancellationToken cancellationToken)
        => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO client_experience_daily
                (day_utc, environment, city_slug, client_release, measurement_version, time_basis, series_kind, series_code,
                 event_count, eligible_starts, succeeded, failed, deadline_exceeded, unresolved, excluded,
                 latency_le_500_ms, latency_le_1000_ms, latency_le_5000_ms, latency_le_15000_ms,
                 latency_le_30000_ms, latency_le_60000_ms, latency_overflow, quality_count, page_session_days)
            SELECT (start_received_at_utc AT TIME ZONE 'UTC')::date, environment, city_slug, client_release, measurement_version,
                   'receipt', 'journey', COALESCE(journey, action, 'unknown'), COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND outcome IN ('succeeded','failed','deadline_exceeded','unresolved'))::bigint,
                   COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND outcome = 'succeeded')::bigint,
                   COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND outcome = 'failed')::bigint,
                   COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND outcome = 'deadline_exceeded')::bigint,
                   COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND outcome = 'unresolved')::bigint,
                   COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND outcome IN ('cancelled','superseded'))::bigint,
                   COUNT(*) FILTER (WHERE outcome = 'succeeded' AND foreground_elapsed_ms <= 500)::bigint,
                   COUNT(*) FILTER (WHERE outcome = 'succeeded' AND foreground_elapsed_ms > 500 AND foreground_elapsed_ms <= 1000)::bigint,
                   COUNT(*) FILTER (WHERE outcome = 'succeeded' AND foreground_elapsed_ms > 1000 AND foreground_elapsed_ms <= 5000)::bigint,
                   COUNT(*) FILTER (WHERE outcome = 'succeeded' AND foreground_elapsed_ms > 5000 AND foreground_elapsed_ms <= 15000)::bigint,
                   COUNT(*) FILTER (WHERE outcome = 'succeeded' AND foreground_elapsed_ms > 15000 AND foreground_elapsed_ms <= 30000)::bigint,
                   COUNT(*) FILTER (WHERE outcome = 'succeeded' AND foreground_elapsed_ms > 30000 AND foreground_elapsed_ms <= 60000)::bigint,
                   COUNT(*) FILTER (WHERE outcome = 'succeeded' AND foreground_elapsed_ms > 60000)::bigint,
                   COUNT(*) FILTER (WHERE logical_conflict)::bigint, COUNT(DISTINCT page_session_id)::bigint
            FROM client_experience_attempts
            WHERE start_received_at_utc >= ({startDate}::date::timestamp AT TIME ZONE 'UTC') AND (eligible OR logical_conflict)
            GROUP BY (start_received_at_utc AT TIME ZONE 'UTC')::date, environment, city_slug, client_release, measurement_version,
                     COALESCE(journey, action, 'unknown')
            """, cancellationToken);

    static async Task RebuildCapabilityRowsAsync(AppDbContext db, DateOnly startDate, CancellationToken cancellationToken)
        => await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH per_window AS (
                SELECT w.event_id, w.environment, w.city_slug, w.client_release, w.measurement_version,
                       w.page_session_id, (w.received_at_utc AT TIME ZONE 'UTC')::date AS day_utc,
                       state.value ->> 'capability' AS capability,
                       SUM(NULLIF(state.value ->> 'durationMs', '')::bigint) FILTER (WHERE state.value ->> 'state' = 'healthy') AS healthy_ms,
                       SUM(NULLIF(state.value ->> 'durationMs', '')::bigint) FILTER (WHERE state.value ->> 'state' = 'degraded') AS degraded_ms,
                       SUM(NULLIF(state.value ->> 'durationMs', '')::bigint) FILTER (WHERE state.value ->> 'state' = 'unavailable') AS unavailable_ms,
                       SUM(NULLIF(state.value ->> 'durationMs', '')::bigint) FILTER (WHERE state.value ->> 'state' = 'pending') AS pending_ms,
                       SUM(NULLIF(state.value ->> 'durationMs', '')::bigint) FILTER (WHERE state.value ->> 'state' = 'unknown') AS unknown_ms,
                       w.observed_ms, w.gap_ms, w.excluded_ms
                FROM client_experience_validated_windows w
                CROSS JOIN LATERAL jsonb_array_elements(w.state_durations) state(value)
                WHERE w.received_at_utc >= ({startDate}::date::timestamp AT TIME ZONE 'UTC')
                GROUP BY w.event_id, w.environment, w.city_slug, w.client_release, w.measurement_version,
                         w.page_session_id, (w.received_at_utc AT TIME ZONE 'UTC')::date, state.value ->> 'capability',
                         w.observed_ms, w.gap_ms, w.excluded_ms
            )
            INSERT INTO client_experience_daily
                (day_utc, environment, city_slug, client_release, measurement_version, time_basis, series_kind, series_code,
                 event_count, observed_ms, healthy_ms, degraded_ms, unavailable_ms, pending_ms, unknown_ms, gap_ms, excluded_ms,
                 page_session_days)
            SELECT day_utc, environment, city_slug, client_release, measurement_version, 'receipt', 'capability', capability,
                   COUNT(*)::bigint, SUM(observed_ms), SUM(COALESCE(healthy_ms,0)), SUM(COALESCE(degraded_ms,0)),
                   SUM(COALESCE(unavailable_ms,0)), SUM(COALESCE(pending_ms,0)), SUM(COALESCE(unknown_ms,0)),
                   SUM(gap_ms), SUM(excluded_ms), COUNT(DISTINCT page_session_id)::bigint
            FROM per_window
            GROUP BY day_utc, environment, city_slug, client_release, measurement_version, capability
            """, cancellationToken);

    static async Task RebuildPresentationRowsAsync(AppDbContext db, DateOnly startDate, CancellationToken cancellationToken)
        => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO client_experience_daily
                (day_utc, environment, city_slug, client_release, measurement_version, time_basis, series_kind, series_code,
                 event_count, presentations, page_session_days)
            SELECT (received_at_utc AT TIME ZONE 'UTC')::date, environment, city_slug, client_release, measurement_version,
                   'receipt', 'presentation', displayed_severity, COUNT(*)::bigint, COUNT(*)::bigint,
                   COUNT(DISTINCT page_session_id)::bigint
            FROM client_experience_presentations
            WHERE received_at_utc >= ({startDate}::date::timestamp AT TIME ZONE 'UTC')
            GROUP BY (received_at_utc AT TIME ZONE 'UTC')::date, environment, city_slug, client_release, measurement_version, displayed_severity
            """, cancellationToken);

    static async Task RebuildQualityRowsAsync(AppDbContext db, DateOnly startDate, CancellationToken cancellationToken)
        => await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH quality AS (
                SELECT environment, city_slug, client_release, measurement_version, page_session_id, received_at_utc,
                       quality_code FROM client_experience_attempt_quality
                UNION ALL
                SELECT environment, city_slug, client_release, measurement_version, page_session_id, received_at_utc,
                       quality_code FROM client_experience_window_quality
                UNION ALL
                SELECT environment, city_slug, client_release, measurement_version, page_session_id, received_at_utc,
                       quality_code FROM client_experience_presentation_quality
            )
            INSERT INTO client_experience_daily
                (day_utc, environment, city_slug, client_release, measurement_version, time_basis, series_kind, series_code,
                 event_count, quality_count, page_session_days)
            SELECT (received_at_utc AT TIME ZONE 'UTC')::date, environment, city_slug, client_release, measurement_version,
                   'receipt', 'quality', quality_code, COUNT(*)::bigint, COUNT(*)::bigint,
                   COUNT(DISTINCT page_session_id)::bigint
            FROM quality
            WHERE received_at_utc >= ({startDate}::date::timestamp AT TIME ZONE 'UTC')
            GROUP BY (received_at_utc AT TIME ZONE 'UTC')::date, environment, city_slug, client_release, measurement_version, quality_code
            """, cancellationToken);

    static async Task RebuildCollectionRowsAsync(AppDbContext db, DateOnly startDate, CancellationToken cancellationToken)
        => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO client_experience_daily
                (day_utc, environment, city_slug, client_release, measurement_version, time_basis, series_kind, series_code,
                 event_count, quality_count, page_session_days)
            SELECT (received_at_utc AT TIME ZONE 'UTC')::date, environment, city_slug, client_release, measurement_version,
                   'receipt', 'collection', 'events', COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE clock_skewed)::bigint, COUNT(DISTINCT page_session_id)::bigint
            FROM client_experience_events
            WHERE received_at_utc >= ({startDate}::date::timestamp AT TIME ZONE 'UTC')
            GROUP BY (received_at_utc AT TIME ZONE 'UTC')::date, environment, city_slug, client_release, measurement_version
            """, cancellationToken);

    async Task CleanupDailyAsync(AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = DateOnly.FromDateTime(now.UtcDateTime).AddMonths(-dailyRetentionMonths);
        await DeleteBatchesAsync(db,
            $"DELETE FROM client_experience_daily WHERE (day_utc, environment, city_slug, client_release, measurement_version, time_basis, series_kind, series_code) IN (SELECT day_utc, environment, city_slug, client_release, measurement_version, time_basis, series_kind, series_code FROM client_experience_daily WHERE day_utc < DATE '{cutoff:yyyy-MM-dd}' ORDER BY day_utc LIMIT {CleanupBatchSize})",
            cancellationToken);
    }

    async Task<int> CleanupRawAsync(AppDbContext db, DateOnly cutoff, CancellationToken cancellationToken)
    {
        var deleted = 0;
        while (deleted < maximumCleanupRowsPerRun)
        {
            var batchLimit = Math.Min(CleanupBatchSize, maximumCleanupRowsPerRun - deleted);
            var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM client_experience_events
                WHERE event_id IN (
                    SELECT event_id FROM client_experience_events
                    WHERE received_at_utc < {cutoff.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)}
                    ORDER BY received_at_utc LIMIT {batchLimit})
                """, cancellationToken);
            deleted += count;
            if (count < batchLimit) break;
        }
        return deleted;
    }

    static async Task DeleteBatchesAsync(AppDbContext db, string sql, CancellationToken cancellationToken)
    {
        while (await db.Database.ExecuteSqlRawAsync(sql, cancellationToken) == CleanupBatchSize) { }
    }

}
