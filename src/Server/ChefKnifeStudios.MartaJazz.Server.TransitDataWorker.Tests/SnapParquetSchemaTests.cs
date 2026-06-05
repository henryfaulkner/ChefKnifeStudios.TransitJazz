using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class SnapParquetSchemaTests
{
    [Fact]
    public async Task SnapSchema_ColumnsMatchContract_OutcomeIsEnumName_NullableRoundTrip()
    {
        var now = new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc);

        var rows = new[]
        {
            new SnapEventArgs
            {
                CycleId = "cycle-1",
                ObservationUtc = now,
                VehicleId = "v001",
                RouteId = "R1",
                SnapOutcome = nameof(SnapDecision.Moved),
                RawLat = 33.749, RawLon = -84.388,
                SnappedLat = 33.750, SnappedLon = -84.390,
                SnapDistanceKm = 0.1, SnapIndex = 5, RoutePointCount = 100,
                SpeedMps = 12.5, BearingDeg = 90.0,
                IsStale = false
            },
            new SnapEventArgs
            {
                CycleId = "cycle-1",
                ObservationUtc = now,
                VehicleId = "v002",
                RouteId = "R2",
                SnapOutcome = nameof(SnapDecision.Stale),
                RawLat = 33.760, RawLon = -84.400,
                SnappedLat = 33.762, SnappedLon = -84.401,
                SnapDistanceKm = 0.2, SnapIndex = 12, RoutePointCount = 50,
                SpeedMps = null, BearingDeg = null,
                IsStale = true
            }
        }.ToList();

        // ── Write ──────────────────────────────────────────────────────────
        using var ms = new MemoryStream();
        var schema = BuildSnapSchema();
        using (var writer = await ParquetWriter.CreateAsync(schema, ms))
        {
            writer.CompressionMethod = CompressionMethod.Snappy;
            using var writerRg = writer.CreateRowGroup();
            var f = schema.DataFields;
            await writerRg.WriteColumnAsync(new DataColumn(f[0], rows.Select(r => r.CycleId).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[1], rows.Select(r => r.ObservationUtc).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[2], rows.Select(r => r.VehicleId).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[3], rows.Select(r => r.RouteId).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[4], rows.Select(r => r.SnapOutcome).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[5], rows.Select(r => r.RawLat).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[6], rows.Select(r => r.RawLon).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[7], rows.Select(r => r.SnappedLat).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[8], rows.Select(r => r.SnappedLon).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[9], rows.Select(r => r.SnapDistanceKm).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[10], rows.Select(r => r.SnapIndex).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[11], rows.Select(r => r.RoutePointCount).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[12], rows.Select(r => r.SpeedMps).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[13], rows.Select(r => r.BearingDeg).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[14], rows.Select(r => r.IsStale).ToArray()));
        }

        // ── Read ──────────────────────────────────────────────────────────
        ms.Position = 0;
        using var reader = await ParquetReader.CreateAsync(ms);

        var contractNames = new[]
        {
            TelemetryColumns.CycleId, TelemetryColumns.ObservationUtc, TelemetryColumns.VehicleId,
            TelemetryColumns.RouteId, TelemetryColumns.SnapOutcome, TelemetryColumns.RawLat,
            TelemetryColumns.RawLon, TelemetryColumns.SnappedLat, TelemetryColumns.SnappedLon,
            TelemetryColumns.SnapDistanceKm, TelemetryColumns.SnapIndex, TelemetryColumns.RoutePointCount,
            TelemetryColumns.SpeedMps, TelemetryColumns.BearingDeg, TelemetryColumns.IsStale
        };

        Assert.Equal(contractNames.Length, reader.Schema.Fields.Count);
        for (int i = 0; i < contractNames.Length; i++)
            Assert.Equal(contractNames[i], reader.Schema.Fields[i].Name);

        using var rg = reader.OpenRowGroupReader(0);
        var readFields = reader.Schema.DataFields;

        // snap_outcome must be the enum name string
        var outcomes = (await rg.ReadColumnAsync(readFields[4])).Data as string[];
        Assert.NotNull(outcomes);
        Assert.Equal(nameof(SnapDecision.Moved), outcomes[0]);
        Assert.Equal(nameof(SnapDecision.Stale), outcomes[1]);

        // nullable speed_mps: first has value, second is null
        var speeds = (await rg.ReadColumnAsync(readFields[12])).Data as double?[];
        Assert.NotNull(speeds);
        Assert.Equal(12.5, speeds[0]);
        Assert.Null(speeds[1]);
    }

    static ParquetSchema BuildSnapSchema() => new(
        new DataField<string>(TelemetryColumns.CycleId),
        new DataField<DateTime>(TelemetryColumns.ObservationUtc),
        new DataField<string>(TelemetryColumns.VehicleId),
        new DataField<string>(TelemetryColumns.RouteId),
        new DataField<string>(TelemetryColumns.SnapOutcome),
        new DataField<double>(TelemetryColumns.RawLat),
        new DataField<double>(TelemetryColumns.RawLon),
        new DataField<double>(TelemetryColumns.SnappedLat),
        new DataField<double>(TelemetryColumns.SnappedLon),
        new DataField<double>(TelemetryColumns.SnapDistanceKm),
        new DataField<int>(TelemetryColumns.SnapIndex),
        new DataField<int>(TelemetryColumns.RoutePointCount),
        new DataField<double?>(TelemetryColumns.SpeedMps),
        new DataField<double?>(TelemetryColumns.BearingDeg),
        new DataField<bool>(TelemetryColumns.IsStale)
    );
}
