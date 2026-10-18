using System.Data;
using System.Text;
using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Statistics;

public enum ReportedVehicleWriteResult { Inserted, Unchanged, Quarantined }

/// <summary>Atomic immutable header/population storage with sticky conflict quarantine.</summary>
public sealed class CityReportedVehicleHoursStore(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider)
{
    const int MaxMembershipsPerCityHour = 100_000;
    const int CommandTimeoutSeconds = 5;
    const int MaxRowsPerCommand = 128;
    public async Task<ReportedVehicleWriteResult> WriteAsync(CityReportedVehicleHour incomingHeader,
        IReadOnlyCollection<CityReportedVehicleHourMembership> incomingMemberships,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incomingHeader);
        ArgumentNullException.ThrowIfNull(incomingMemberships);
        ValidateBatch(incomingHeader, incomingMemberships);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Database.SetCommandTimeout(CommandTimeoutSeconds);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await AcquireLockAsync(connection, transaction, incomingHeader, cancellationToken);

        var existing = await context.CityReportedVehicleHours
            .Include(x => x.Memberships)
            .SingleOrDefaultAsync(x => x.CitySlug == incomingHeader.CitySlug && x.HourStartUtc == incomingHeader.HourStartUtc,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.HasConflict)
            {
                await transaction.CommitAsync(cancellationToken);
                return ReportedVehicleWriteResult.Quarantined;
            }
            if (SemanticallyEquals(existing, incomingHeader, incomingMemberships))
            {
                await transaction.CommitAsync(cancellationToken);
                return ReportedVehicleWriteResult.Unchanged;
            }
            existing.HasConflict = true;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ReportedVehicleWriteResult.Quarantined;
        }

        var header = CopyHeader(incomingHeader, timeProvider.GetUtcNow().UtcDateTime);
        context.CityReportedVehicleHours.Add(header);
        await context.SaveChangesAsync(cancellationToken);
        await InsertMembershipsAsync(connection, transaction, incomingHeader, incomingMemberships, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReportedVehicleWriteResult.Inserted;
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int maxHeaders = 24,
        CancellationToken cancellationToken = default)
    {
        if (cutoffUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Retention cutoff must be UTC.", nameof(cutoffUtc));
        if (maxHeaders is < 1 or > 24) throw new ArgumentOutOfRangeException(nameof(maxHeaders));
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Database.SetCommandTimeout(CommandTimeoutSeconds);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH expired AS (
                SELECT city_slug, hour_start_utc
                FROM public.city_reported_vehicle_hours
                WHERE hour_start_utc < @cutoff
                ORDER BY hour_start_utc, city_slug COLLATE "C"
                LIMIT @max_rows
                FOR UPDATE SKIP LOCKED
            )
            DELETE FROM public.city_reported_vehicle_hours AS h
            USING expired AS e
            WHERE h.city_slug = e.city_slug AND h.hour_start_utc = e.hour_start_utc;
            """, connection);
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Parameters.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, cutoffUtc);
        command.Parameters.AddWithValue("max_rows", NpgsqlDbType.Integer, maxHeaders);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    async Task AcquireLockAsync(NpgsqlConnection connection, IDbContextTransaction transaction,
        CityReportedVehicleHour header, CancellationToken cancellationToken)
    {
        var dbTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0))", connection, dbTransaction);
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Parameters.AddWithValue("lock_key", $"reported_vehicle_hour:{header.CitySlug}:{header.HourStartUtc:O}");
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    async Task InsertMembershipsAsync(NpgsqlConnection connection, IDbContextTransaction transaction,
        CityReportedVehicleHour header, IReadOnlyCollection<CityReportedVehicleHourMembership> memberships,
        CancellationToken cancellationToken)
    {
        var dbTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        var ordered = memberships.OrderBy(x => x.IdentityNamespace, StringComparer.Ordinal)
            .ThenBy(x => x.IdentityKind, StringComparer.Ordinal).ThenBy(x => x.IdentityValue, StringComparer.Ordinal).ToArray();
        for (var offset = 0; offset < ordered.Length; offset += MaxRowsPerCommand)
        {
            var count = Math.Min(MaxRowsPerCommand, ordered.Length - offset);
            var rows = new string[count];
            await using var command = new NpgsqlCommand { Connection = connection, Transaction = dbTransaction };
            command.CommandTimeout = CommandTimeoutSeconds;
            for (var index = 0; index < count; index++)
            {
                var parameter = index * 5;
                var identity = ordered[offset + index];
                rows[index] = $"(@p{parameter}, @p{parameter + 1}, @p{parameter + 2}, @p{parameter + 3}, @p{parameter + 4})";
                command.Parameters.AddWithValue($"p{parameter}", NpgsqlDbType.Varchar, header.CitySlug);
                command.Parameters.AddWithValue($"p{parameter + 1}", NpgsqlDbType.TimestampTz, header.HourStartUtc);
                command.Parameters.AddWithValue($"p{parameter + 2}", NpgsqlDbType.Text, identity.IdentityNamespace);
                command.Parameters.AddWithValue($"p{parameter + 3}", NpgsqlDbType.Text, identity.IdentityKind);
                command.Parameters.AddWithValue($"p{parameter + 4}", NpgsqlDbType.Text, identity.IdentityValue);
            }
            command.CommandText = "INSERT INTO public.city_reported_vehicle_hour_memberships "
                + "(city_slug, hour_start_utc, identity_namespace, identity_kind, identity_value) VALUES "
                + string.Join(",", rows);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    static CityReportedVehicleHour CopyHeader(CityReportedVehicleHour header, DateTime persistedAtUtc) => new()
    {
        CitySlug = header.CitySlug,
        HourStartUtc = header.HourStartUtc,
        DefinitionVersion = header.DefinitionVersion,
        CaptureRunId = header.CaptureRunId,
        SourceSetFingerprint = header.SourceSetFingerprint,
        CadencePolicyFingerprint = header.CadencePolicyFingerprint,
        CollectionStatus = header.CollectionStatus,
        IdentityIntegrity = header.IdentityIntegrity,
        HasConflict = false,
        MembershipCount = header.MembershipCount,
        SuccessfulPollCount = header.SuccessfulPollCount,
        FailedPollCount = header.FailedPollCount,
        UnidentifiableRecordCount = header.UnidentifiableRecordCount,
        FallbackRecordCount = header.FallbackRecordCount,
        ExpectedSourceCount = header.ExpectedSourceCount,
        SourceDiagnosticsJson = header.SourceDiagnosticsJson,
        IncompleteReasons = header.IncompleteReasons.ToArray(),
        IdentityLimitations = header.IdentityLimitations.ToArray(),
        ObservedStartUtc = header.ObservedStartUtc,
        ObservedEndUtc = header.ObservedEndUtc,
        PersistedAtUtc = persistedAtUtc,
    };

    static bool SemanticallyEquals(CityReportedVehicleHour existing, CityReportedVehicleHour header,
        IReadOnlyCollection<CityReportedVehicleHourMembership> memberships)
    {
        if (existing.CitySlug != header.CitySlug || existing.HourStartUtc != header.HourStartUtc
            || existing.DefinitionVersion != header.DefinitionVersion || existing.CaptureRunId != header.CaptureRunId
            || existing.SourceSetFingerprint != header.SourceSetFingerprint || existing.CadencePolicyFingerprint != header.CadencePolicyFingerprint
            || existing.CollectionStatus.ToString() != header.CollectionStatus.ToString()
            || existing.IdentityIntegrity != header.IdentityIntegrity || existing.MembershipCount != header.MembershipCount
            || existing.SuccessfulPollCount != header.SuccessfulPollCount || existing.FailedPollCount != header.FailedPollCount
            || existing.UnidentifiableRecordCount != header.UnidentifiableRecordCount || existing.FallbackRecordCount != header.FallbackRecordCount
            || existing.ExpectedSourceCount != header.ExpectedSourceCount
            || CanonicalJson(existing.SourceDiagnosticsJson) != CanonicalJson(header.SourceDiagnosticsJson)
            || !existing.IncompleteReasons.Order(StringComparer.Ordinal).SequenceEqual(header.IncompleteReasons.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !existing.IdentityLimitations.Order(StringComparer.Ordinal).SequenceEqual(header.IdentityLimitations.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || existing.ObservedStartUtc != header.ObservedStartUtc || existing.ObservedEndUtc != header.ObservedEndUtc)
            return false;

        var stored = existing.Memberships.OrderBy(x => x.IdentityNamespace, StringComparer.Ordinal)
            .ThenBy(x => x.IdentityKind, StringComparer.Ordinal).ThenBy(x => x.IdentityValue, StringComparer.Ordinal).ToArray();
        var incoming = memberships.OrderBy(x => x.IdentityNamespace, StringComparer.Ordinal)
            .ThenBy(x => x.IdentityKind, StringComparer.Ordinal).ThenBy(x => x.IdentityValue, StringComparer.Ordinal).ToArray();
        return stored.Length == incoming.Length && stored.Zip(incoming).All(pair =>
            string.Equals(pair.First.IdentityNamespace, pair.Second.IdentityNamespace, StringComparison.Ordinal)
            && string.Equals(pair.First.IdentityKind, pair.Second.IdentityKind, StringComparison.Ordinal)
            && string.Equals(pair.First.IdentityValue, pair.Second.IdentityValue, StringComparison.Ordinal));
    }

    static void ValidateBatch(CityReportedVehicleHour header, IReadOnlyCollection<CityReportedVehicleHourMembership> memberships)
    {
        if (string.IsNullOrWhiteSpace(header.CitySlug) || header.CitySlug.Length > 64
            || header.HourStartUtc.Kind != DateTimeKind.Utc || header.HourStartUtc.Minute != 0
            || header.HourStartUtc.Second != 0 || header.HourStartUtc.Ticks % TimeSpan.TicksPerHour != 0
            || header.CaptureRunId == Guid.Empty || header.MembershipCount != memberships.Count
            || header.MembershipCount is < 0 or > MaxMembershipsPerCityHour
            || !Enum.IsDefined(header.CollectionStatus)
            || System.Text.Encoding.UTF8.GetByteCount(header.SourceDiagnosticsJson) > 16 * 1024)
            throw new ArgumentException("Reported-vehicle header or population is outside its finite bounds.");
        using (JsonDocument.Parse(header.SourceDiagnosticsJson)) { }

        var unique = new HashSet<(string Namespace, string Kind, string Value)>();
        foreach (var row in memberships)
        {
            if (row.CitySlug != header.CitySlug || row.HourStartUtc != header.HourStartUtc
                || string.IsNullOrWhiteSpace(row.IdentityNamespace) || string.IsNullOrWhiteSpace(row.IdentityKind)
                || string.IsNullOrWhiteSpace(row.IdentityValue)
                || row.IdentityNamespace.IndexOf('\0') >= 0 || row.IdentityKind.IndexOf('\0') >= 0 || row.IdentityValue.IndexOf('\0') >= 0
                || System.Text.Encoding.UTF8.GetByteCount(row.IdentityNamespace) > 64
                || System.Text.Encoding.UTF8.GetByteCount(row.IdentityKind) > 64
                || System.Text.Encoding.UTF8.GetByteCount(row.IdentityValue) > 512
                || !unique.Add((row.IdentityNamespace, row.IdentityKind, row.IdentityValue)))
                throw new ArgumentException("Reported-vehicle membership is invalid or duplicated.");
        }
    }

    static string CanonicalJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(document.RootElement, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(element.GetRawText()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new ArgumentException("Unsupported JSON token in source diagnostics.");
        }
    }
}
