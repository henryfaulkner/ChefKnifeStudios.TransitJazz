using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Statistics;

public sealed record CategoryStatisticsWriteReport(int Inserted, int Unchanged, int Conflicts, int BackingMinutesUnavailable)
{
    public int Total => Inserted + Unchanged + Conflicts + BackingMinutesUnavailable;
}

public sealed class CityCategoryStatisticsStore(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task<CategoryStatisticsWriteReport> WriteAsync(
        IReadOnlyCollection<CityCategoryMinuteStatistic> minutes,
        IReadOnlyCollection<CityCategoryHourStatistic> hours,
        int commandTimeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        ArgumentNullException.ThrowIfNull(hours);
        var minuteRows = NormalizeMinutes(minutes);
        var hourRows = NormalizeHours(hours);
        var inserted = 0;
        var unchanged = 0;
        var conflicts = 0;
        var unavailable = 0;
        var hoursToQuarantine = new HashSet<HourKey>();
        var backingEvidence = new Dictionary<HourKey, bool>();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Database.SetCommandTimeout(commandTimeoutSeconds);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var backingHours = hourRows.Where(x => x.CollectionStatus == CategoryCollectionStatus.Complete)
            .Select(x => new HourKey(x.CitySlug, x.Category, x.HourStartUtc)).Distinct()
            .OrderBy(x => x.CitySlug, StringComparer.Ordinal).ThenBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.HourStartUtc).ToArray();
        // Lock the union of caller-supplied minute keys and every minute key a Complete candidate
        // reads before inserting or quarantining any hour. A single ordered statement keeps two
        // overlapping batches from locking complementary minute subsets in opposite order.
        await LockMinuteUnionAsync(context, minuteRows, backingHours, cancellationToken);

        foreach (var row in minuteRows)
        {
            row.Validate();
            var added = await InsertMinuteAsync(context, row, cancellationToken);
            if (added)
            {
                inserted++;
                if (row.HasConflict)
                {
                    conflicts++;
                    hoursToQuarantine.Add(new(row.CitySlug, row.Category, HourStart(row.StatMinuteUtc)));
                }
                continue;
            }

            var existing = await context.CityCategoryMinuteStatistics.FromSqlInterpolated($"""
                SELECT * FROM public.city_category_minute_statistics
                WHERE city_slug = {row.CitySlug} AND category = {row.Category} AND stat_minute_utc = {row.StatMinuteUtc}
                FOR UPDATE
                """).AsNoTracking().SingleAsync(cancellationToken);
            if (SamePayload(existing, row) && !row.HasConflict)
            {
                unchanged++;
                continue;
            }

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public.city_category_minute_statistics SET has_conflict = TRUE
                WHERE city_slug = {row.CitySlug} AND category = {row.Category} AND stat_minute_utc = {row.StatMinuteUtc}
                """, cancellationToken);
            hoursToQuarantine.Add(new(row.CitySlug, row.Category, HourStart(row.StatMinuteUtc)));
            conflicts++;
        }

        // Acquire all minute backing locks before touching an hour row. This preserves the global
        // minute-then-hour lock order used by concurrent writers.
        foreach (var row in hourRows.Where(x => x.CollectionStatus == CategoryCollectionStatus.Complete))
        {
            var key = new HourKey(row.CitySlug, row.Category, row.HourStartUtc);
            backingEvidence[key] = await HasReconciledMinutesAsync(context, row, cancellationToken);
        }

        var hourLockKeys = hourRows.Select(x => new HourKey(x.CitySlug, x.Category, x.HourStartUtc))
            .Concat(hoursToQuarantine).Distinct().OrderBy(x => x.CitySlug, StringComparer.Ordinal)
            .ThenBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.HourStartUtc).ToArray();
        foreach (var key in hourLockKeys)
            await LockHourAsync(context, key, cancellationToken);
        foreach (var key in hoursToQuarantine.OrderBy(x => x.CitySlug, StringComparer.Ordinal).ThenBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.HourStartUtc))
            await QuarantineHourAsync(context, key, cancellationToken);

        foreach (var row in hourRows)
        {
            row.Validate();
            var existing = await context.CityCategoryHourStatistics.FromSqlInterpolated($"""
                SELECT * FROM public.city_category_hour_statistics
                WHERE city_slug = {row.CitySlug} AND category = {row.Category} AND hour_start_utc = {row.HourStartUtc}
                FOR UPDATE
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                if (SamePayload(existing, row) && !row.HasConflict)
                    unchanged++;
                else
                {
                    await QuarantineHourAsync(context, new(row.CitySlug, row.Category, row.HourStartUtc), cancellationToken);
                    conflicts++;
                }
                continue;
            }

            if (row.CollectionStatus == CategoryCollectionStatus.Complete
                && !backingEvidence.GetValueOrDefault(new(row.CitySlug, row.Category, row.HourStartUtc)))
            {
                unavailable++;
                continue;
            }

            if (await InsertHourAsync(context, row, cancellationToken))
                inserted++;
            else
            {
                // A competing transaction inserted after our pre-lock lookup. A separate locked
                // read sees its committed row, then applies the same immutable comparison.
                var raced = await context.CityCategoryHourStatistics.FromSqlInterpolated($"""
                    SELECT * FROM public.city_category_hour_statistics
                    WHERE city_slug = {row.CitySlug} AND category = {row.Category} AND hour_start_utc = {row.HourStartUtc}
                    FOR UPDATE
                    """).AsNoTracking().SingleAsync(cancellationToken);
                if (SamePayload(raced, row) && !row.HasConflict) unchanged++;
                else
                {
                    await QuarantineHourAsync(context, new(row.CitySlug, row.Category, row.HourStartUtc), cancellationToken);
                    conflicts++;
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new(inserted, unchanged, conflicts, unavailable);
    }

    static async Task<bool> InsertMinuteAsync(AppDbContext context, CityCategoryMinuteStatistic x, CancellationToken ct) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.city_category_minute_statistics
            (city_slug, category, stat_minute_utc, definition_version, collection_status, has_conflict, healthy_cadence_limit_seconds,
             observed_cycle_count, valid_active_sample_count, valid_publish_cycle_count, failed_cycle_count, first_cycle_utc, last_cycle_utc,
             max_observation_gap_seconds, active_vehicle_count_sum, distance_meters_sum, distance_interval_count, distance_rejected_count, crossings_published_count)
            VALUES ({x.CitySlug}, {x.Category}, {x.StatMinuteUtc}, {x.DefinitionVersion}, {x.CollectionStatus.ToString()}, {x.HasConflict}, {x.HealthyCadenceLimitSeconds},
             {x.ObservedCycleCount}, {x.ValidActiveSampleCount}, {x.ValidPublishCycleCount}, {x.FailedCycleCount}, {x.FirstCycleUtc}, {x.LastCycleUtc},
             {x.MaxObservationGapSeconds}, {x.ActiveVehicleCountSum}, {x.DistanceMetersSum}, {x.DistanceIntervalCount}, {x.DistanceRejectedCount}, {x.CrossingsPublishedCount})
            ON CONFLICT (city_slug, category, stat_minute_utc) DO NOTHING
            """, ct) == 1;

    static async Task<bool> InsertHourAsync(AppDbContext context, CityCategoryHourStatistic x, CancellationToken ct) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.city_category_hour_statistics
            (city_slug, category, hour_start_utc, definition_version, collection_status, has_conflict, healthy_cadence_limit_seconds,
             observed_cycle_count, valid_active_sample_count, valid_publish_cycle_count, failed_cycle_count, first_cycle_utc, last_cycle_utc,
             max_observation_gap_seconds, active_vehicle_count_sum, distance_meters_sum, distance_interval_count, distance_rejected_count,
             crossings_published_count, distinct_active_vehicle_count, covered_minutes)
            VALUES ({x.CitySlug}, {x.Category}, {x.HourStartUtc}, {x.DefinitionVersion}, {x.CollectionStatus.ToString()}, {x.HasConflict}, {x.HealthyCadenceLimitSeconds},
             {x.ObservedCycleCount}, {x.ValidActiveSampleCount}, {x.ValidPublishCycleCount}, {x.FailedCycleCount}, {x.FirstCycleUtc}, {x.LastCycleUtc},
             {x.MaxObservationGapSeconds}, {x.ActiveVehicleCountSum}, {x.DistanceMetersSum}, {x.DistanceIntervalCount}, {x.DistanceRejectedCount},
             {x.CrossingsPublishedCount}, {x.DistinctActiveVehicleCount}, {x.CoveredMinutes})
            ON CONFLICT (city_slug, category, hour_start_utc) DO NOTHING
            """, ct) == 1;

    static async Task<bool> HasReconciledMinutesAsync(AppDbContext context, CityCategoryHourStatistic hour, CancellationToken ct)
    {
        var end = hour.HourStartUtc.AddHours(1);
        var rows = await context.CityCategoryMinuteStatistics.FromSqlInterpolated($"""
            SELECT * FROM public.city_category_minute_statistics
            WHERE city_slug = {hour.CitySlug} AND category = {hour.Category}
              AND stat_minute_utc >= {hour.HourStartUtc} AND stat_minute_utc < {end}
            ORDER BY stat_minute_utc FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
        if (rows.Count != 60 || rows.Any(x => x.CollectionStatus != CategoryCollectionStatus.Complete || x.HasConflict
            || x.DefinitionVersion != hour.DefinitionVersion || x.HealthyCadenceLimitSeconds != hour.HealthyCadenceLimitSeconds)) return false;
        return rows.Sum(x => x.ObservedCycleCount) == hour.ObservedCycleCount
            && rows.Sum(x => x.ValidActiveSampleCount) == hour.ValidActiveSampleCount
            && rows.Sum(x => x.ValidPublishCycleCount) == hour.ValidPublishCycleCount
            && rows.Sum(x => x.FailedCycleCount) == hour.FailedCycleCount
            && rows.Sum(x => x.ActiveVehicleCountSum) == hour.ActiveVehicleCountSum
            && rows.Sum(x => x.DistanceMetersSum) == hour.DistanceMetersSum
            && rows.Sum(x => x.DistanceIntervalCount) == hour.DistanceIntervalCount
            && rows.Sum(x => x.DistanceRejectedCount) == hour.DistanceRejectedCount
            && rows.Sum(x => x.CrossingsPublishedCount) == hour.CrossingsPublishedCount;
    }

    static DateTime HourStart(DateTime minute) => new(minute.Year, minute.Month, minute.Day, minute.Hour, 0, 0, DateTimeKind.Utc);

    static async Task LockHourAsync(AppDbContext context, HourKey key, CancellationToken ct)
    {
        _ = await context.CityCategoryHourStatistics.FromSqlInterpolated($"""
            SELECT * FROM public.city_category_hour_statistics
            WHERE city_slug = {key.CitySlug} AND category = {key.Category} AND hour_start_utc = {key.HourStartUtc}
            FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
    }

    static async Task LockMinuteUnionAsync(AppDbContext context, IReadOnlyCollection<CityCategoryMinuteStatistic> minuteRows,
        IReadOnlyCollection<HourKey> backingHours, CancellationToken ct)
    {
        var ranges = backingHours.Select(x => (x.CitySlug, x.Category, Start: x.HourStartUtc, End: x.HourStartUtc.AddHours(1)))
            .ToArray();
        var exactKeys = minuteRows.Select(x => new MinuteKey(x.CitySlug, x.Category, x.StatMinuteUtc))
            .Where(key => !ranges.Any(range => range.CitySlug == key.CitySlug && range.Category == key.Category
                && key.StatMinuteUtc >= range.Start && key.StatMinuteUtc < range.End))
            .Distinct().OrderBy(x => x.CitySlug, StringComparer.Ordinal).ThenBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.StatMinuteUtc).ToArray();
        if (ranges.Length == 0 && exactKeys.Length == 0) return;

        var clauses = new List<string>(ranges.Length + exactKeys.Length);
        var parameters = new List<NpgsqlParameter>();
        void AddClause(string sql, params object[] values)
        {
            var names = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var name = "p" + parameters.Count;
                names[i] = "@" + name;
                parameters.Add(new NpgsqlParameter(name, values[i]));
            }
            clauses.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, names));
        }
        foreach (var range in ranges)
            AddClause("(city_slug = {0} AND category = {1} AND stat_minute_utc >= {2} AND stat_minute_utc < {3})",
                range.CitySlug, range.Category, range.Start, range.End);
        foreach (var key in exactKeys)
            AddClause("(city_slug = {0} AND category = {1} AND stat_minute_utc = {2})", key.CitySlug, key.Category, key.StatMinuteUtc);

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 5;
        command.CommandText = $"SELECT city_slug, category, stat_minute_utc FROM public.city_category_minute_statistics WHERE {string.Join(" OR ", clauses)} ORDER BY city_slug, category, stat_minute_utc FOR UPDATE";
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { }
    }

    static Task<int> QuarantineHourAsync(AppDbContext context, HourKey key, CancellationToken ct) => context.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE public.city_category_hour_statistics SET has_conflict = TRUE
        WHERE city_slug = {key.CitySlug} AND category = {key.Category} AND hour_start_utc = {key.HourStartUtc}
        """, ct);

    static List<CityCategoryMinuteStatistic> NormalizeMinutes(IReadOnlyCollection<CityCategoryMinuteStatistic> input)
    {
        var rows = new Dictionary<MinuteKey, CityCategoryMinuteStatistic>();
        var duplicateConflicts = new HashSet<MinuteKey>();
        foreach (var candidate in input)
        {
            candidate.Validate(); // precision is rejected instead of allowing PostgreSQL to round a frozen payload
            var key = new MinuteKey(candidate.CitySlug, candidate.Category, candidate.StatMinuteUtc);
            if (!rows.TryGetValue(key, out var current)) rows.Add(key, Copy(candidate));
            else
            {
                if (!SamePayload(current, candidate)) duplicateConflicts.Add(key);
                current.HasConflict |= candidate.HasConflict;
            }
        }
        foreach (var key in duplicateConflicts) rows[key].HasConflict = true;
        return rows.OrderBy(x => x.Key.CitySlug, StringComparer.Ordinal).ThenBy(x => x.Key.Category, StringComparer.Ordinal).ThenBy(x => x.Key.StatMinuteUtc).Select(x => x.Value).ToList();
    }

    static List<CityCategoryHourStatistic> NormalizeHours(IReadOnlyCollection<CityCategoryHourStatistic> input)
    {
        var rows = new Dictionary<HourKey, CityCategoryHourStatistic>();
        var duplicateConflicts = new HashSet<HourKey>();
        foreach (var candidate in input)
        {
            candidate.Validate();
            var key = new HourKey(candidate.CitySlug, candidate.Category, candidate.HourStartUtc);
            if (!rows.TryGetValue(key, out var current)) rows.Add(key, Copy(candidate));
            else
            {
                if (!SamePayload(current, candidate)) duplicateConflicts.Add(key);
                current.HasConflict |= candidate.HasConflict;
            }
        }
        foreach (var key in duplicateConflicts) rows[key].HasConflict = true;
        return rows.OrderBy(x => x.Key.CitySlug, StringComparer.Ordinal).ThenBy(x => x.Key.Category, StringComparer.Ordinal).ThenBy(x => x.Key.HourStartUtc).Select(x => x.Value).ToList();
    }

    static CityCategoryMinuteStatistic Copy(CityCategoryMinuteStatistic x) => new()
    {
        CitySlug = x.CitySlug, Category = x.Category, StatMinuteUtc = x.StatMinuteUtc, DefinitionVersion = x.DefinitionVersion,
        CollectionStatus = x.CollectionStatus, HasConflict = x.HasConflict, HealthyCadenceLimitSeconds = x.HealthyCadenceLimitSeconds,
        ObservedCycleCount = x.ObservedCycleCount, ValidActiveSampleCount = x.ValidActiveSampleCount,
        ValidPublishCycleCount = x.ValidPublishCycleCount, FailedCycleCount = x.FailedCycleCount,
        FirstCycleUtc = x.FirstCycleUtc, LastCycleUtc = x.LastCycleUtc, MaxObservationGapSeconds = x.MaxObservationGapSeconds,
        ActiveVehicleCountSum = x.ActiveVehicleCountSum, DistanceMetersSum = x.DistanceMetersSum,
        DistanceIntervalCount = x.DistanceIntervalCount, DistanceRejectedCount = x.DistanceRejectedCount,
        CrossingsPublishedCount = x.CrossingsPublishedCount,
    };

    static CityCategoryHourStatistic Copy(CityCategoryHourStatistic x) => new()
    {
        CitySlug = x.CitySlug, Category = x.Category, HourStartUtc = x.HourStartUtc, DefinitionVersion = x.DefinitionVersion,
        CollectionStatus = x.CollectionStatus, HasConflict = x.HasConflict, HealthyCadenceLimitSeconds = x.HealthyCadenceLimitSeconds,
        ObservedCycleCount = x.ObservedCycleCount, ValidActiveSampleCount = x.ValidActiveSampleCount,
        ValidPublishCycleCount = x.ValidPublishCycleCount, FailedCycleCount = x.FailedCycleCount,
        FirstCycleUtc = x.FirstCycleUtc, LastCycleUtc = x.LastCycleUtc, MaxObservationGapSeconds = x.MaxObservationGapSeconds,
        ActiveVehicleCountSum = x.ActiveVehicleCountSum, DistanceMetersSum = x.DistanceMetersSum,
        DistanceIntervalCount = x.DistanceIntervalCount, DistanceRejectedCount = x.DistanceRejectedCount,
        CrossingsPublishedCount = x.CrossingsPublishedCount, DistinctActiveVehicleCount = x.DistinctActiveVehicleCount,
        CoveredMinutes = x.CoveredMinutes,
    };

    readonly record struct MinuteKey(string CitySlug, string Category, DateTime StatMinuteUtc);
    readonly record struct HourKey(string CitySlug, string Category, DateTime HourStartUtc);

    static bool SamePayload(CityCategoryMinuteStatistic a, CityCategoryMinuteStatistic b) =>
        a.CitySlug == b.CitySlug && a.Category == b.Category && a.StatMinuteUtc == b.StatMinuteUtc
        && a.DefinitionVersion == b.DefinitionVersion && a.CollectionStatus == b.CollectionStatus
        && a.HealthyCadenceLimitSeconds == b.HealthyCadenceLimitSeconds && a.ObservedCycleCount == b.ObservedCycleCount
        && a.ValidActiveSampleCount == b.ValidActiveSampleCount && a.ValidPublishCycleCount == b.ValidPublishCycleCount
        && a.FailedCycleCount == b.FailedCycleCount && a.FirstCycleUtc == b.FirstCycleUtc && a.LastCycleUtc == b.LastCycleUtc
        && a.MaxObservationGapSeconds == b.MaxObservationGapSeconds && a.ActiveVehicleCountSum == b.ActiveVehicleCountSum
        && a.DistanceMetersSum == b.DistanceMetersSum && a.DistanceIntervalCount == b.DistanceIntervalCount
        && a.DistanceRejectedCount == b.DistanceRejectedCount && a.CrossingsPublishedCount == b.CrossingsPublishedCount;

    static bool SamePayload(CityCategoryHourStatistic a, CityCategoryHourStatistic b) =>
        a.CitySlug == b.CitySlug && a.Category == b.Category && a.HourStartUtc == b.HourStartUtc
        && a.DefinitionVersion == b.DefinitionVersion && a.CollectionStatus == b.CollectionStatus
        && a.HealthyCadenceLimitSeconds == b.HealthyCadenceLimitSeconds && a.ObservedCycleCount == b.ObservedCycleCount
        && a.ValidActiveSampleCount == b.ValidActiveSampleCount && a.ValidPublishCycleCount == b.ValidPublishCycleCount
        && a.FailedCycleCount == b.FailedCycleCount && a.FirstCycleUtc == b.FirstCycleUtc && a.LastCycleUtc == b.LastCycleUtc
        && a.MaxObservationGapSeconds == b.MaxObservationGapSeconds && a.ActiveVehicleCountSum == b.ActiveVehicleCountSum
        && a.DistanceMetersSum == b.DistanceMetersSum && a.DistanceIntervalCount == b.DistanceIntervalCount
        && a.DistanceRejectedCount == b.DistanceRejectedCount && a.CrossingsPublishedCount == b.CrossingsPublishedCount
        && a.DistinctActiveVehicleCount == b.DistinctActiveVehicleCount && a.CoveredMinutes == b.CoveredMinutes;
}
