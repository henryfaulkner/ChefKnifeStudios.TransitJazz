using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class LerpParquetSchemaTests
{
    [Fact]
    public async Task LerpSchema_ColumnsMatchContract_NullableDeltasRoundTrip()
    {
        var now = new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc);
        var prior = now.AddSeconds(-10);

        var rows = new[]
        {
            new LerpEventArgs
            {
                CycleId = "cycle-1",
                ObservationUtc = now,
                VehicleId = "v001",
                PriorRouteId = "R1",
                PriorSnappedLat = 33.749, PriorSnappedLon = -84.388,
                PriorObservationUtc = prior,
                PriorSpeedMps = 10.0, PriorBearingDeg = 45.0,
                PosDeltaKm = 0.15,
                SpeedDelta = 2.5, BearingDelta = -5.0,
                TimeDeltaSec = 10.0
            },
            new LerpEventArgs
            {
                CycleId = "cycle-1",
                ObservationUtc = now,
                VehicleId = "v002",
                PriorRouteId = "R2",
                PriorSnappedLat = 33.760, PriorSnappedLon = -84.400,
                PriorObservationUtc = prior,
                PriorSpeedMps = null, PriorBearingDeg = null,
                PosDeltaKm = 0.05,
                SpeedDelta = null, BearingDelta = null,
                TimeDeltaSec = 10.0
            }
        }.ToList();

        // ── Write ──────────────────────────────────────────────────────────
        using var ms = new MemoryStream();
        var schema = BuildLerpSchema();
        using (var writer = await ParquetWriter.CreateAsync(schema, ms))
        {
            writer.CompressionMethod = CompressionMethod.Snappy;
            using var writerRg = writer.CreateRowGroup();
            var f = schema.DataFields;
            await writerRg.WriteColumnAsync(new DataColumn(f[0], rows.Select(r => r.CycleId).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[1], rows.Select(r => r.ObservationUtc).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[2], rows.Select(r => r.VehicleId).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[3], rows.Select(r => r.PriorRouteId).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[4], rows.Select(r => r.PriorSnappedLat).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[5], rows.Select(r => r.PriorSnappedLon).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[6], rows.Select(r => r.PriorObservationUtc).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[7], rows.Select(r => r.PriorSpeedMps).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[8], rows.Select(r => r.PriorBearingDeg).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[9], rows.Select(r => r.PosDeltaKm).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[10], rows.Select(r => r.SpeedDelta).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[11], rows.Select(r => r.BearingDelta).ToArray()));
            await writerRg.WriteColumnAsync(new DataColumn(f[12], rows.Select(r => r.TimeDeltaSec).ToArray()));
        }

        // ── Read ──────────────────────────────────────────────────────────
        ms.Position = 0;
        using var reader = await ParquetReader.CreateAsync(ms);

        var contractNames = new[]
        {
            TelemetryColumns.CycleId, TelemetryColumns.ObservationUtc, TelemetryColumns.VehicleId,
            TelemetryColumns.PriorRouteId, TelemetryColumns.PriorSnappedLat, TelemetryColumns.PriorSnappedLon,
            TelemetryColumns.PriorObservationUtc, TelemetryColumns.PriorSpeedMps, TelemetryColumns.PriorBearingDeg,
            TelemetryColumns.PosDeltaKm, TelemetryColumns.SpeedDelta, TelemetryColumns.BearingDelta,
            TelemetryColumns.TimeDeltaSec
        };

        Assert.Equal(contractNames.Length, reader.Schema.Fields.Count);
        for (int i = 0; i < contractNames.Length; i++)
            Assert.Equal(contractNames[i], reader.Schema.Fields[i].Name);

        using var rg = reader.OpenRowGroupReader(0);
        var readFields = reader.Schema.DataFields;

        var vehicleIds = (await rg.ReadColumnAsync(readFields[2])).Data as string[];
        Assert.NotNull(vehicleIds);
        Assert.Equal(2, vehicleIds.Length);
        Assert.Equal("v001", vehicleIds[0]);

        // nullable speed_delta: first=2.5, second=null
        var speedDelta = (await rg.ReadColumnAsync(readFields[10])).Data as double?[];
        Assert.NotNull(speedDelta);
        Assert.Equal(2.5, speedDelta[0]);
        Assert.Null(speedDelta[1]);

        // nullable bearing_delta: first=-5.0, second=null
        var bearingDelta = (await rg.ReadColumnAsync(readFields[11])).Data as double?[];
        Assert.NotNull(bearingDelta);
        Assert.Equal(-5.0, bearingDelta[0]);
        Assert.Null(bearingDelta[1]);
    }

    static ParquetSchema BuildLerpSchema() => new(
        new DataField<string>(TelemetryColumns.CycleId),
        new DataField<DateTime>(TelemetryColumns.ObservationUtc),
        new DataField<string>(TelemetryColumns.VehicleId),
        new DataField<string>(TelemetryColumns.PriorRouteId),
        new DataField<double>(TelemetryColumns.PriorSnappedLat),
        new DataField<double>(TelemetryColumns.PriorSnappedLon),
        new DataField<DateTime>(TelemetryColumns.PriorObservationUtc),
        new DataField<double?>(TelemetryColumns.PriorSpeedMps),
        new DataField<double?>(TelemetryColumns.PriorBearingDeg),
        new DataField<double>(TelemetryColumns.PosDeltaKm),
        new DataField<double?>(TelemetryColumns.SpeedDelta),
        new DataField<double?>(TelemetryColumns.BearingDelta),
        new DataField<double>(TelemetryColumns.TimeDeltaSec)
    );
}
