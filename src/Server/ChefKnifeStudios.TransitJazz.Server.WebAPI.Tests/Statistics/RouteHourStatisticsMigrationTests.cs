using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Text.Json;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class RouteHourStatisticsMigrationTests
{
    [Fact]
    public void ModelAndMigrationContainOnlyTheNewRouteHourTable()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=transitjazz")
            .Options;
        using var context = new AppDbContext(options);

        var migrations = context.Database.GetMigrations().ToArray();
        var routeMigrationId = Assert.Single(migrations, id => id.EndsWith("_CreateCityRouteHourStatistics", StringComparison.Ordinal));
        Assert.Equal(1, migrations.Count(id => id.EndsWith("_CreateCityRouteHourStatistics", StringComparison.Ordinal)));

        var entity = context.Model.FindEntityType(typeof(CityRouteHourStatistic))!;
        Assert.Equal("city_route_hour_statistics", entity.GetTableName());
        Assert.Equal(new[] { "CitySlug", "RouteJoinKey", "HourStartUtc" },
            entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(1, entity.GetIndexes().Count());

        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var migrationType = migrationsAssembly.Migrations[routeMigrationId];
        var migration = migrationsAssembly.CreateMigration(migrationType, "Npgsql.EntityFrameworkCore.PostgreSQL");
        var createTable = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("city_route_hour_statistics", createTable.Name);
        Assert.Empty(migration.UpOperations.OfType<DropTableOperation>());
        Assert.Equal(1, migration.UpOperations.OfType<CreateIndexOperation>().Count());
    }

    [Fact]
    public void SourceEvidenceMigrationIsAdditiveAndKeepsLegacyRowsNullable()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=transitjazz")
            .Options;
        using var context = new AppDbContext(options);
        var migrationId = Assert.Single(context.Database.GetMigrations(), id =>
            id.EndsWith("_AddRouteHourSourceEvidence", StringComparison.Ordinal));
        var migrationType = context.GetService<IMigrationsAssembly>().Migrations[migrationId];
        var migration = context.GetService<IMigrationsAssembly>()
            .CreateMigration(migrationType, "Npgsql.EntityFrameworkCore.PostgreSQL");
        var addedColumns = migration.UpOperations.OfType<AddColumnOperation>().ToArray();

        Assert.Equal(new[]
        {
            "activity_reasons", "activity_support_fingerprint", "activity_valid", "distance_reasons",
            "distance_valid", "publication_reasons", "publication_valid", "source_evidence", "source_set_fingerprint"
        }, addedColumns.Select(column => column.Name));
        Assert.All(addedColumns, column => Assert.True(column.IsNullable));
        Assert.Contains(addedColumns, column => column.Name == "source_evidence" && column.ColumnType == "jsonb");
        Assert.Contains(migration.UpOperations.OfType<AddCheckConstraintOperation>(), operation =>
            operation.Name == "ck_city_route_hour_v2_evidence");
        Assert.DoesNotContain(migration.UpOperations, operation => operation is UpdateDataOperation or DeleteDataOperation);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is DropTableOperation or DropColumnOperation);
    }

    [Fact]
    public void RouteHourSqlRecipeIsCopiedToTestOutput()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "route-hour-insights.sql"));
        Assert.Contains("city_route_hour_statistics", sql, StringComparison.Ordinal);
        Assert.Contains("generate_series", sql, StringComparison.Ordinal);
        Assert.Contains("typical_local_hours", sql, StringComparison.Ordinal);
    }

    [RouteHourPostgresFact]
    public async Task PreFeatureUpgradePreservesLegacyRowsAndStoresAllV2EvidenceAtomically()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateBeforeSourceEvidenceAsync();
        await using (var legacy = database.CreateDbContext())
        {
            await legacy.Database.ExecuteSqlRawAsync(LegacyRowSql("legacy-complete", "Complete", "ARRAY[]::text[]", true));
            await legacy.Database.ExecuteSqlRawAsync(LegacyRowSql("legacy-partial", "Partial", "ARRAY['boundary_unproven']::text[]", false));
        }

        await database.ApplyLatestMigrationsAsync();
        await using (var upgraded = database.CreateDbContext())
        {
            var retained = await upgraded.CityRouteHourStatistics.AsNoTracking()
                .Where(row => row.RouteJoinKey.StartsWith("legacy-"))
                .OrderBy(row => row.RouteJoinKey).ToArrayAsync();
            Assert.Equal(["legacy-complete", "legacy-partial"], retained.Select(row => row.RouteJoinKey));
            Assert.Equal([RouteHourCollectionStatus.Complete, RouteHourCollectionStatus.Partial], retained.Select(row => row.CollectionStatus));
            Assert.All(retained, row =>
            {
                Assert.Equal(CityRouteHourStatistic.CurrentDefinitionVersion, row.DefinitionVersion);
                Assert.Null(row.SourceEvidence);
                Assert.Null(row.DistanceValid);
                Assert.Null(row.ActivityValid);
                Assert.Null(row.PublicationValid);
                Assert.Null(row.DistanceReasons);
                Assert.Null(row.ActivityReasons);
                Assert.Null(row.PublicationReasons);
                Assert.Null(row.SourceSetFingerprint);
                Assert.Null(row.ActivitySupportFingerprint);
            });

            var evidence = NewV2Row("v2-roundtrip", "b");
            evidence.Validate();
            upgraded.CityRouteHourStatistics.Add(evidence);
            await upgraded.SaveChangesAsync();
        }

        await using (var verify = database.CreateDbContext())
        {
            var roundtrip = await verify.CityRouteHourStatistics.AsNoTracking().SingleAsync(row => row.RouteJoinKey == "v2-roundtrip");
            Assert.Equal(CityRouteHourStatistic.SourceEvidenceDefinitionVersion, roundtrip.DefinitionVersion);
            Assert.Equal(new string('b', 64), roundtrip.SourceSetFingerprint);
            using (var sourceEvidence = JsonDocument.Parse(roundtrip.SourceEvidence!))
            {
                Assert.Equal("route-hour-source-evidence-v1", sourceEvidence.RootElement.GetProperty("schemaVersion").GetString());
                Assert.Equal(new string('b', 64), sourceEvidence.RootElement.GetProperty("sourceSetFingerprint").GetString());
                Assert.Equal("marta-bus", sourceEvidence.RootElement.GetProperty("sources")[0].GetProperty("sourceKey").GetString());
            }
            Assert.True(roundtrip.DistanceValid);
            Assert.True(roundtrip.ActivityValid);
            Assert.False(roundtrip.PublicationValid);
            Assert.Equal(["publication_unavailable"], roundtrip.PublicationReasons);
            Assert.Equal("c".PadLeft(64, 'c'), roundtrip.ActivitySupportFingerprint);
        }

        await using (var rejected = database.CreateDbContext())
        {
            var valid = NewV2Row("v2-atomic-good", "d");
            var invalid = NewV2Row("v2-atomic-bad", "e");
            invalid.SourceEvidence = NewV2Row("v2-atomic-bad", "d").SourceEvidence;
            rejected.CityRouteHourStatistics.AddRange(valid, invalid);
            await Assert.ThrowsAsync<DbUpdateException>(() => rejected.SaveChangesAsync());
        }

        await using var final = database.CreateDbContext();
        Assert.False(await final.CityRouteHourStatistics.AnyAsync(row =>
            row.RouteJoinKey == "v2-atomic-good" || row.RouteJoinKey == "v2-atomic-bad"));
        Assert.True(await final.CityRouteHourStatistics.AnyAsync(row => row.RouteJoinKey == "v2-roundtrip"));
    }

    static string LegacyRowSql(string key, string status, string reasons, bool complete) => $"""
        INSERT INTO public.city_route_hour_statistics (
            city_slug, route_join_key, hour_start_utc, route_short_name, static_route_id, category,
            route_catalog_fingerprint, catalog_changed, definition_version, capture_run_id,
            collection_status, has_conflict, incomplete_reasons, healthy_cadence_limit_seconds,
            observed_cycle_count, valid_active_sample_count, valid_publish_cycle_count, failed_cycle_count,
            first_cycle_utc, last_cycle_utc, max_observation_gap_seconds, start_boundary_ok, end_boundary_ok,
            active_vehicle_count_sum, peak_active_vehicle_count, distinct_active_vehicle_count,
            vehicle_observations_processed_count, stale_observations_count, distance_meters_sum,
            distance_interval_count, distance_rejected_count, crossings_detected_count, crossings_published_count,
            crossings_suppressed_first_seen, crossings_suppressed_delta_leq_zero, crossings_suppressed_teleport,
            crossings_suppressed_transfer)
        VALUES (
            'atlanta', '{key}', '2026-11-07T12:00:00Z', '{key}', 'shape-{key}', 'bus', repeat('a', 64), false,
            '{CityRouteHourStatistic.CurrentDefinitionVersion}', 'ed6150e0-b01d-4c16-8cf1-5c06826b1b1a'::uuid,
            '{status}', false, {reasons}, 10, 1, 1, {(complete ? 1 : 0)}, 0,
            '2026-11-07T12:05:00Z', '2026-11-07T12:05:00Z', 10, {complete.ToString().ToLowerInvariant()},
            {complete.ToString().ToLowerInvariant()}, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        """;

    static CityRouteHourStatistic NewV2Row(string key, string fingerprintFill)
    {
        var fingerprint = new string(fingerprintFill[0], 64);
        var row = new CityRouteHourStatistic
        {
            CitySlug = "atlanta", RouteJoinKey = key,
            HourStartUtc = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc),
            RouteShortName = key, StaticRouteId = "shape-" + key, Category = "bus",
            RouteCatalogFingerprint = new string('a', 64),
            DefinitionVersion = CityRouteHourStatistic.SourceEvidenceDefinitionVersion,
            CaptureRunId = Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
            CollectionStatus = RouteHourCollectionStatus.Partial,
            IncompleteReasons = ["source_failure"], HealthyCadenceLimitSeconds = 10,
            ObservedCycleCount = 1, ValidActiveSampleCount = 1, ValidPublishCycleCount = 1,
            FirstCycleUtc = new DateTime(2026, 11, 7, 12, 5, 0, DateTimeKind.Utc),
            LastCycleUtc = new DateTime(2026, 11, 7, 12, 5, 0, DateTimeKind.Utc),
            MaxObservationGapSeconds = 10, ActiveVehicleCountSum = 0, PeakActiveVehicleCount = 0,
            DistinctActiveVehicleCount = 0,
            SourceSetFingerprint = fingerprint, DistanceValid = true, ActivityValid = true,
            PublicationValid = false, DistanceReasons = [], ActivityReasons = [],
            PublicationReasons = ["publication_unavailable"], ActivitySupportFingerprint = new string('c', 64)
        };
        row.SourceEvidence = JsonSerializer.Serialize(new
        {
            schemaVersion = "route-hour-source-evidence-v1",
            sourceSetFingerprint = fingerprint,
            expectedSourceKeys = new[] { "marta-bus" },
            contributingSourceKeys = new[] { "marta-bus" },
            membershipKnown = true,
            sources = new[]
            {
                new
                {
                    sourceKey = "marta-bus", successfulPollCount = 1, emptyPollCount = 0, failedPollCount = 0,
                    disabledPollCount = 0, firstSuccessfulReceiptUtc = row.FirstCycleUtc,
                    lastSuccessfulReceiptUtc = row.LastCycleUtc, maxSuccessfulGapSeconds = 10,
                    startBoundaryOk = true, endBoundaryOk = true, positionedCoverageValid = true,
                    recoveredDuringHour = false, reasons = Array.Empty<string>()
                }
            }
        });
        return row;
    }
}
