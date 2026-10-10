using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Statistics;

public enum RouteHourStatisticsWriteOutcome { Inserted, Unchanged, Quarantined }

public sealed record RouteHourStatisticsWriteReport(RouteHourStatisticsWriteOutcome Outcome, int Rows);

public interface ICityRouteHourStatisticsStore
{
    Task<RouteHourStatisticsWriteReport> WriteAsync(IReadOnlyCollection<CityRouteHourStatistic> rows,
        int maxRowsPerCommand = 128, int commandTimeoutSeconds = 5, int maxRoutesPerCityHour = 4096,
        CancellationToken cancellationToken = default);
}

/// <summary>Atomic immutable whole-city/hour retention with sticky cohort quarantine.</summary>
public sealed class CityRouteHourStatisticsStore(IDbContextFactory<AppDbContext> contextFactory) : ICityRouteHourStatisticsStore
{
    public async Task<RouteHourStatisticsWriteReport> WriteAsync(
        IReadOnlyCollection<CityRouteHourStatistic> rows,
        int maxRowsPerCommand = 128,
        int commandTimeoutSeconds = 5,
        int maxRoutesPerCityHour = 4096,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (maxRowsPerCommand is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(maxRowsPerCommand));
        if (commandTimeoutSeconds is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
        if (maxRoutesPerCityHour is < 1 or > 16_384) throw new ArgumentOutOfRangeException(nameof(maxRoutesPerCityHour));
        var payload = Normalize(rows, maxRoutesPerCityHour);
        var first = payload[0];

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Database.SetCommandTimeout(commandTimeoutSeconds);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await SetLocalTimeoutsAsync(context, commandTimeoutSeconds, cancellationToken);
        await AcquireEnvelopeLockAsync(context, first.CitySlug, first.HourStartUtc, commandTimeoutSeconds, cancellationToken);

        // Keep this as a distinct statement after the advisory lock. Read committed gives this
        // lookup a fresh statement snapshot after any wait for an earlier producer to commit.
        var existing = await context.CityRouteHourStatistics.AsNoTracking()
            .Where(row => row.CitySlug == first.CitySlug && row.HourStartUtc == first.HourStartUtc)
            .OrderBy(row => row.RouteJoinKey)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            if (SameCohort(existing, payload))
            {
                await transaction.CommitAsync(cancellationToken);
                return new(RouteHourStatisticsWriteOutcome.Unchanged, existing.Count);
            }

            var tracked = await context.CityRouteHourStatistics
                .Where(row => row.CitySlug == first.CitySlug && row.HourStartUtc == first.HourStartUtc)
                .ToListAsync(cancellationToken);
            foreach (var row in tracked) row.HasConflict = true;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(RouteHourStatisticsWriteOutcome.Quarantined, existing.Count);
        }

        foreach (var chunk in payload.Chunk(maxRowsPerCommand))
        {
            context.CityRouteHourStatistics.AddRange(chunk);
            await context.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(RouteHourStatisticsWriteOutcome.Inserted, payload.Count);
    }

    public static long AdvisoryLockKey(string citySlug, DateTime hourStartUtc)
    {
        if (string.IsNullOrWhiteSpace(citySlug)) throw new ArgumentException("City slug is required.", nameof(citySlug));
        if (hourStartUtc.Kind != DateTimeKind.Utc || hourStartUtc.Minute != 0 || hourStartUtc.Second != 0
            || hourStartUtc.Ticks % TimeSpan.TicksPerHour != 0)
            throw new ArgumentException("Hour key must be aligned UTC.", nameof(hourStartUtc));
        using var stream = new MemoryStream();
        WriteString(stream, "route-hour-statistics-v1");
        WriteString(stream, citySlug);
        WriteString(stream, hourStartUtc.ToString("O", CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(stream.ToArray());
        return BinaryPrimitives.ReadInt64BigEndian(hash.AsSpan(0, sizeof(long)));
    }

    static List<CityRouteHourStatistic> Normalize(IReadOnlyCollection<CityRouteHourStatistic> rows, int maxRows)
    {
        if (rows.Count == 0) throw new ArgumentException("A city/hour envelope must contain at least one row.", nameof(rows));
        if (rows.Count > maxRows) throw new ArgumentException("Route-hour cohort exceeds its configured route limit.", nameof(rows));
        var normalized = rows.Select(Copy).OrderBy(row => row.RouteJoinKey, StringComparer.Ordinal).ToList();
        var first = normalized[0];
        string? priorKey = null;
        foreach (var row in normalized)
        {
            row.Validate();
            if (row.HasConflict) throw new ArgumentException("Incoming capture rows cannot set the monotone conflict flag.", nameof(rows));
            if (!string.Equals(row.CitySlug, first.CitySlug, StringComparison.Ordinal)
                || row.HourStartUtc != first.HourStartUtc || row.CaptureRunId != first.CaptureRunId)
                throw new ArgumentException("Rows must share one city, UTC hour and capture run.", nameof(rows));
            if (priorKey is not null && StringComparer.Ordinal.Compare(priorKey, row.RouteJoinKey) >= 0)
                throw new ArgumentException("Route keys must be unique within a cohort.", nameof(rows));
            priorKey = row.RouteJoinKey;
        }
        return normalized;
    }

    static bool SameCohort(IReadOnlyList<CityRouteHourStatistic> stored, IReadOnlyList<CityRouteHourStatistic> incoming)
    {
        if (stored.Count != incoming.Count) return false;
        for (var i = 0; i < stored.Count; i++)
            if (!SamePayload(stored[i], incoming[i])) return false;
        return true;
    }

    static bool SamePayload(CityRouteHourStatistic a, CityRouteHourStatistic b) =>
        string.Equals(a.CitySlug, b.CitySlug, StringComparison.Ordinal)
        && string.Equals(a.RouteJoinKey, b.RouteJoinKey, StringComparison.Ordinal)
        && a.HourStartUtc == b.HourStartUtc
        && string.Equals(a.RouteShortName, b.RouteShortName, StringComparison.Ordinal)
        && string.Equals(a.StaticRouteId, b.StaticRouteId, StringComparison.Ordinal)
        && string.Equals(a.Category, b.Category, StringComparison.Ordinal)
        && string.Equals(a.RouteCatalogFingerprint, b.RouteCatalogFingerprint, StringComparison.Ordinal)
        && a.CatalogChanged == b.CatalogChanged
        && string.Equals(a.DefinitionVersion, b.DefinitionVersion, StringComparison.Ordinal)
        && a.CaptureRunId == b.CaptureRunId
        && a.CollectionStatus == b.CollectionStatus
        && a.HealthyCadenceLimitSeconds == b.HealthyCadenceLimitSeconds
        && a.ObservedCycleCount == b.ObservedCycleCount
        && a.ValidActiveSampleCount == b.ValidActiveSampleCount
        && a.ValidPublishCycleCount == b.ValidPublishCycleCount
        && a.FailedCycleCount == b.FailedCycleCount
        && a.FirstCycleUtc == b.FirstCycleUtc
        && a.LastCycleUtc == b.LastCycleUtc
        && a.MaxObservationGapSeconds == b.MaxObservationGapSeconds
        && a.StartBoundaryOk == b.StartBoundaryOk
        && a.EndBoundaryOk == b.EndBoundaryOk
        && a.ActiveVehicleCountSum == b.ActiveVehicleCountSum
        && a.PeakActiveVehicleCount == b.PeakActiveVehicleCount
        && a.DistinctActiveVehicleCount == b.DistinctActiveVehicleCount
        && a.VehicleObservationsProcessedCount == b.VehicleObservationsProcessedCount
        && a.StaleObservationsCount == b.StaleObservationsCount
        && a.DistanceMetersSum == b.DistanceMetersSum
        && a.DistanceIntervalCount == b.DistanceIntervalCount
        && a.DistanceRejectedCount == b.DistanceRejectedCount
        && a.CrossingsDetectedCount == b.CrossingsDetectedCount
        && a.CrossingsPublishedCount == b.CrossingsPublishedCount
        && a.CrossingsSuppressedFirstSeen == b.CrossingsSuppressedFirstSeen
        && a.CrossingsSuppressedDeltaLeqZero == b.CrossingsSuppressedDeltaLeqZero
        && a.CrossingsSuppressedTeleport == b.CrossingsSuppressedTeleport
        && a.CrossingsSuppressedTransfer == b.CrossingsSuppressedTransfer
        && a.IncompleteReasons.SequenceEqual(b.IncompleteReasons, StringComparer.Ordinal);

    static CityRouteHourStatistic Copy(CityRouteHourStatistic row) => new()
    {
        CitySlug = row.CitySlug,
        RouteJoinKey = row.RouteJoinKey,
        HourStartUtc = row.HourStartUtc,
        RouteShortName = row.RouteShortName,
        StaticRouteId = row.StaticRouteId,
        Category = row.Category,
        RouteCatalogFingerprint = row.RouteCatalogFingerprint,
        CatalogChanged = row.CatalogChanged,
        DefinitionVersion = row.DefinitionVersion,
        CaptureRunId = row.CaptureRunId,
        CollectionStatus = row.CollectionStatus,
        HasConflict = row.HasConflict,
        IncompleteReasons = row.IncompleteReasons?.ToArray() ?? [],
        HealthyCadenceLimitSeconds = row.HealthyCadenceLimitSeconds,
        ObservedCycleCount = row.ObservedCycleCount,
        ValidActiveSampleCount = row.ValidActiveSampleCount,
        ValidPublishCycleCount = row.ValidPublishCycleCount,
        FailedCycleCount = row.FailedCycleCount,
        FirstCycleUtc = row.FirstCycleUtc,
        LastCycleUtc = row.LastCycleUtc,
        MaxObservationGapSeconds = row.MaxObservationGapSeconds,
        StartBoundaryOk = row.StartBoundaryOk,
        EndBoundaryOk = row.EndBoundaryOk,
        ActiveVehicleCountSum = row.ActiveVehicleCountSum,
        PeakActiveVehicleCount = row.PeakActiveVehicleCount,
        DistinctActiveVehicleCount = row.DistinctActiveVehicleCount,
        VehicleObservationsProcessedCount = row.VehicleObservationsProcessedCount,
        StaleObservationsCount = row.StaleObservationsCount,
        DistanceMetersSum = row.DistanceMetersSum,
        DistanceIntervalCount = row.DistanceIntervalCount,
        DistanceRejectedCount = row.DistanceRejectedCount,
        CrossingsDetectedCount = row.CrossingsDetectedCount,
        CrossingsPublishedCount = row.CrossingsPublishedCount,
        CrossingsSuppressedFirstSeen = row.CrossingsSuppressedFirstSeen,
        CrossingsSuppressedDeltaLeqZero = row.CrossingsSuppressedDeltaLeqZero,
        CrossingsSuppressedTeleport = row.CrossingsSuppressedTeleport,
        CrossingsSuppressedTransfer = row.CrossingsSuppressedTransfer,
    };

    static async Task SetLocalTimeoutsAsync(AppDbContext context, int commandTimeoutSeconds, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(context, "SELECT set_config('statement_timeout', @statement_timeout, true), set_config('lock_timeout', @lock_timeout, true)");
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.AddWithValue("statement_timeout", $"{commandTimeoutSeconds * 1000}ms");
        command.Parameters.AddWithValue("lock_timeout", $"{commandTimeoutSeconds * 1000}ms");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    static async Task AcquireEnvelopeLockAsync(AppDbContext context, string citySlug, DateTime hourStartUtc,
        int commandTimeoutSeconds, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(context, "SELECT pg_advisory_xact_lock(@lock_key)");
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.Add("lock_key", NpgsqlDbType.Bigint).Value = AdvisoryLockKey(citySlug, hourStartUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    static NpgsqlCommand CreateCommand(AppDbContext context, string sql)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)context.Database.CurrentTransaction!.GetDbTransaction();
        return new NpgsqlCommand(sql, connection, transaction);
    }

    static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }
}
