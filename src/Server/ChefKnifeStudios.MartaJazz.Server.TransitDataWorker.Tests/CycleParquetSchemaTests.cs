using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class CycleParquetSchemaTests
{
    [Fact]
    public async Task CycleSchema_ColumnsMatchContract_AndValuesRoundTrip()
    {
        var start = new DateTime(2026, 6, 4, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(2.5);

        var rows = new[]
        {
            new CycleEventArgs
            {
                CycleId = "abc123",
                CycleStartUtc = start,
                CycleEndUtc = end,
                CycleExecutionSeconds = 2.5,
                BusesProcessed = 100,
                BusesMoved = 40,
                BusesUnchanged = 30,
                BusesStationary = 20,
                BusesStale = 10,
                BusesSkippedNoRouteId = 5,
                BusesSkippedUnknownRoute = 3,
                FeedHeaderTs = 1234567890L,
                DuplicateFeed = false,
                LastUpdateCacheSize = 200,
                VehicleStateCacheSize = 150,
                SidecarBufferOccupancy = 7,
                SidecarDroppedRecords = 2,
                SidecarPersistFailures = 1
            },
            new CycleEventArgs
            {
                CycleId = "def456",
                CycleStartUtc = start,
                CycleEndUtc = end,
                CycleExecutionSeconds = 1.0,
                BusesProcessed = 50,
                BusesMoved = 20,
                BusesUnchanged = 15,
                BusesStationary = 10,
                BusesStale = 5,
                BusesSkippedNoRouteId = 0,
                BusesSkippedUnknownRoute = 0,
                FeedHeaderTs = null,
                DuplicateFeed = true,
                LastUpdateCacheSize = 100,
                VehicleStateCacheSize = 75,
                SidecarBufferOccupancy = 0,
                SidecarDroppedRecords = 0,
                SidecarPersistFailures = 0
            }
        }.ToList();

        // ── Write ──────────────────────────────────────────────────────────
        using var ms = new MemoryStream();
        var schema = BuildCycleSchema();
        using (var writer = await ParquetWriter.CreateAsync(schema, ms))
        {
            writer.CompressionMethod = CompressionMethod.Snappy;
            using var writerRg = writer.CreateRowGroup();
            var f = schema.DataFields;
            await writerRg.WriteColumnAsync(new DataColumn(f[0], rows.Select(r => r.CycleId).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[1], rows.Select(r => r.CycleStartUtc).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[2], rows.Select(r => r.CycleEndUtc).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[3], rows.Select(r => r.CycleExecutionSeconds).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[4], rows.Select(r => r.BusesProcessed).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[5], rows.Select(r => r.BusesMoved).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[6], rows.Select(r => r.BusesUnchanged).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[7], rows.Select(r => r.BusesStationary).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[8], rows.Select(r => r.BusesStale).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[9], rows.Select(r => r.BusesSkippedNoRouteId).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[10], rows.Select(r => r.BusesSkippedUnknownRoute).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[11], rows.Select(r => r.FeedHeaderTs).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[12], rows.Select(r => r.DuplicateFeed).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[13], rows.Select(r => r.LastUpdateCacheSize).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[14], rows.Select(r => r.VehicleStateCacheSize).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[15], rows.Select(r => r.SidecarBufferOccupancy).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[16], rows.Select(r => r.SidecarDroppedRecords).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[17], rows.Select(r => r.SidecarPersistFailures).ToArray()));
        }

        // ── Read ──────────────────────────────────────────────────────────
        ms.Position = 0;
        using var reader = await ParquetReader.CreateAsync(ms);

        var contractNames = new[]
        {
            TelemetryColumns.CycleId, TelemetryColumns.CycleStartUtc, TelemetryColumns.CycleEndUtc,
            TelemetryColumns.CycleExecutionSeconds, TelemetryColumns.BusesProcessed,
            TelemetryColumns.BusesMoved, TelemetryColumns.BusesUnchanged,
            TelemetryColumns.BusesStationary, TelemetryColumns.BusesStale,
            TelemetryColumns.BusesSkippedNoRouteId, TelemetryColumns.BusesSkippedUnknownRoute,
            TelemetryColumns.FeedHeaderTs, TelemetryColumns.DuplicateFeed,
            TelemetryColumns.LastUpdateCacheSize, TelemetryColumns.VehicleStateCacheSize,
            TelemetryColumns.SidecarBufferOccupancy, TelemetryColumns.SidecarDroppedRecords,
            TelemetryColumns.SidecarPersistFailures
        };

        Assert.Equal(contractNames.Length, reader.Schema.Fields.Count);
        for (int i = 0; i < contractNames.Length; i++)
            Assert.Equal(contractNames[i], reader.Schema.Fields[i].Name);

        using var rg = reader.OpenRowGroupReader(0);
        var readSchema = reader.Schema;

        var cycleIds = (await rg.ReadColumnAsync(readSchema.DataFields[0])).Data as string[];
        Assert.NotNull(cycleIds);
        Assert.Equal(2, cycleIds.Length);
        Assert.Equal("abc123", cycleIds[0]);
        Assert.Equal("def456", cycleIds[1]);

        // Nullable FeedHeaderTs: first=1234567890, second=null
        var feedHeaderTs = (await rg.ReadColumnAsync(readSchema.DataFields[11])).Data as long?[];
        Assert.NotNull(feedHeaderTs);
        Assert.Equal(1234567890L, feedHeaderTs[0]);
        Assert.Null(feedHeaderTs[1]);
    }

    static ParquetSchema BuildCycleSchema() => new(
        new DataField<string>(TelemetryColumns.CycleId),
        new DataField<DateTime>(TelemetryColumns.CycleStartUtc),
        new DataField<DateTime>(TelemetryColumns.CycleEndUtc),
        new DataField<double>(TelemetryColumns.CycleExecutionSeconds),
        new DataField<int>(TelemetryColumns.BusesProcessed),
        new DataField<int>(TelemetryColumns.BusesMoved),
        new DataField<int>(TelemetryColumns.BusesUnchanged),
        new DataField<int>(TelemetryColumns.BusesStationary),
        new DataField<int>(TelemetryColumns.BusesStale),
        new DataField<int>(TelemetryColumns.BusesSkippedNoRouteId),
        new DataField<int>(TelemetryColumns.BusesSkippedUnknownRoute),
        new DataField<long?>(TelemetryColumns.FeedHeaderTs),
        new DataField<bool>(TelemetryColumns.DuplicateFeed),
        new DataField<int>(TelemetryColumns.LastUpdateCacheSize),
        new DataField<int>(TelemetryColumns.VehicleStateCacheSize),
        new DataField<int>(TelemetryColumns.SidecarBufferOccupancy),
        new DataField<long>(TelemetryColumns.SidecarDroppedRecords),
        new DataField<long>(TelemetryColumns.SidecarPersistFailures)
    );
}
