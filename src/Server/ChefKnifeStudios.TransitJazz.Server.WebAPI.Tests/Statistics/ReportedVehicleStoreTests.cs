using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class ReportedVehicleStoreTests
{
    [ReportedVehiclePostgresFact]
    public async Task EmptyCohortsAreImmutableRetriesQuarantineConflictsAndRetentionCascades()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync();
        var store = new CityReportedVehicleHoursStore(database, TimeProvider.System);
        var recent = FloorHour(DateTime.UtcNow).AddHours(-1);
        var populatedHour = recent.AddHours(-1);
        var expired = recent.AddDays(-10);

        var empty = Header(recent);
        empty.SourceDiagnosticsJson = "{\"b\":2,\"a\":1}";
        Assert.Equal(ReportedVehicleWriteResult.Inserted,
            await store.WriteAsync(empty, Array.Empty<CityReportedVehicleHourMembership>()));
        var reorderedEmpty = Header(recent);
        reorderedEmpty.SourceDiagnosticsJson = "{ \"a\": 1, \"b\": 2 }";
        Assert.Equal(ReportedVehicleWriteResult.Unchanged,
            await store.WriteAsync(reorderedEmpty,
                Array.Empty<CityReportedVehicleHourMembership>()));

        var membership = Membership(populatedHour, "vehicle-a");
        var populated = Header(populatedHour, membershipCount: 1);
        Assert.Equal(ReportedVehicleWriteResult.Inserted,
            await store.WriteAsync(populated, [membership]));
        Assert.Equal(ReportedVehicleWriteResult.Quarantined,
            await store.WriteAsync(populated, [Membership(populatedHour, "vehicle-b")]));
        Assert.Equal(ReportedVehicleWriteResult.Quarantined,
            await store.WriteAsync(populated, [membership]));

        var expiredMembership = Membership(expired, "expired-vehicle");
        var expiredHeader = Header(expired, membershipCount: 1);
        Assert.Equal(ReportedVehicleWriteResult.Inserted,
            await store.WriteAsync(expiredHeader, [expiredMembership]));
        var deleted = await store.DeleteExpiredAsync(recent.AddHours(-1), 24);
        Assert.Equal(1, deleted);

        await using var context = database.CreateDbContext();
        Assert.True(await context.CityReportedVehicleHours.AnyAsync(x => x.HourStartUtc == populatedHour && x.HasConflict));
        Assert.False(await context.CityReportedVehicleHours.AnyAsync(x => x.HourStartUtc == expired));
        Assert.False(await context.CityReportedVehicleHourMemberships.AnyAsync(x => x.HourStartUtc == expired));
        Assert.Equal(1, await context.CityReportedVehicleHourMemberships.CountAsync(x => x.HourStartUtc == populatedHour));
    }

    [ReportedVehiclePostgresFact]
    public async Task OrdinalPopulationKeysRespectUtf8BoundsAndConcurrentRetriesQuarantineRunChanges()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync();
        var store = new CityReportedVehicleHoursStore(database, TimeProvider.System);
        var hour = FloorHour(DateTime.UtcNow).AddHours(-1);
        var memberships = new[]
        {
            Membership(hour, "A"),
            Membership(hour, "a"),
            new CityReportedVehicleHourMembership
            {
                CitySlug = "fixture-city",
                HourStartUtc = hour,
                IdentityNamespace = new string('é', 32),
                IdentityKind = new string('å', 32),
                IdentityValue = new string('車', 170), // 510 UTF-8 bytes.
            },
        };
        var header = Header(hour, memberships.Length);
        var concurrent = await Task.WhenAll(
            store.WriteAsync(header, memberships),
            store.WriteAsync(header, memberships));
        Assert.Single(concurrent, result => result == ReportedVehicleWriteResult.Inserted);
        Assert.Single(concurrent, result => result == ReportedVehicleWriteResult.Unchanged);

        await using (var context = database.CreateDbContext())
        {
            Assert.Equal(3, await context.CityReportedVehicleHourMemberships.CountAsync(x => x.HourStartUtc == hour));
        }

        var changedRun = Header(hour, memberships.Length);
        changedRun.CaptureRunId = Guid.NewGuid();
        Assert.Equal(ReportedVehicleWriteResult.Quarantined, await store.WriteAsync(changedRun, memberships));
        Assert.Equal(ReportedVehicleWriteResult.Quarantined, await store.WriteAsync(header, memberships));

        var tooLongHour = hour.AddHours(-1);
        var tooLong = Membership(tooLongHour, new string('v', 513));
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(Header(tooLongHour, 1), [tooLong]));
        await using (var context = database.CreateDbContext())
        {
            context.CityReportedVehicleHours.Add(Header(tooLongHour));
            await context.SaveChangesAsync();
            context.CityReportedVehicleHourMemberships.Add(tooLong);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [ReportedVehiclePostgresFact]
    public async Task MembershipChunkFailureRollsBackTheHeaderAndEveryInsertedRow()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync();
        await using (var context = database.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION public.reject_reported_vehicle_membership() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'test membership failure'; END; $$;
                CREATE TRIGGER reject_reported_vehicle_membership
                BEFORE INSERT ON public.city_reported_vehicle_hour_memberships
                FOR EACH ROW EXECUTE FUNCTION public.reject_reported_vehicle_membership();
                """);
        }

        var hour = FloorHour(DateTime.UtcNow).AddHours(-1);
        var memberships = Enumerable.Range(0, 129)
            .Select(index => Membership(hour, $"vehicle-{index:D3}")).ToArray();
        var store = new CityReportedVehicleHoursStore(database, TimeProvider.System);
        await Assert.ThrowsAsync<PostgresException>(() => store.WriteAsync(Header(hour, memberships.Length), memberships));

        await using var verify = database.CreateDbContext();
        Assert.False(await verify.CityReportedVehicleHours.AnyAsync(x => x.HourStartUtc == hour));
        Assert.False(await verify.CityReportedVehicleHourMemberships.AnyAsync(x => x.HourStartUtc == hour));
    }

    static CityReportedVehicleHour Header(DateTime hour, int membershipCount = 0) => new()
    {
        CitySlug = "fixture-city",
        HourStartUtc = hour,
        DefinitionVersion = CityReportedVehicleHour.CurrentDefinitionVersion,
        CaptureRunId = Guid.Parse("34fdb6a0-a3ce-4501-9c25-b3f4118f3091"),
        SourceSetFingerprint = new string('a', 64),
        CadencePolicyFingerprint = new string('b', 64),
        CollectionStatus = ReportedVehicleCollectionStatus.Partial,
        IdentityIntegrity = true,
        MembershipCount = membershipCount,
        SuccessfulPollCount = 1,
        ExpectedSourceCount = 1,
        SourceDiagnosticsJson = "[]",
        IncompleteReasons = ["start_boundary_missing"],
        IdentityLimitations = ["descriptor_identity"],
        PersistedAtUtc = DateTime.UtcNow,
    };

    static CityReportedVehicleHourMembership Membership(DateTime hour, string value) => new()
    {
        CitySlug = "fixture-city",
        HourStartUtc = hour,
        IdentityNamespace = "fixture-agency",
        IdentityKind = "descriptor",
        IdentityValue = value,
    };

    static DateTime FloorHour(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);
}
