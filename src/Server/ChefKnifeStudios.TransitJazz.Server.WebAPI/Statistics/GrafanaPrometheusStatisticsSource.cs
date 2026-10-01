using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.Extensions.Options;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public interface IHistoricalStatisticsSource
{
    Task<StatisticsSourceResult> QueryAsync(DateTime fromMinuteUtc, DateTime toMinuteUtc, CancellationToken cancellationToken = default);
}

public sealed record StatisticsSourceResult(
    IReadOnlyList<CityMinuteStatistic> Rows,
    DateTime ReturnedStartUtc,
    DateTime ReturnedEndUtc,
    IReadOnlyCollection<string> Warnings);

public sealed class StatisticsSourceException(
    string message,
    string code = "metrics-source-failure",
    string fieldName = "",
    int? httpStatusCode = null,
    string causeType = null) : Exception(message)
{
    public string Code { get; } = code;
    public string FieldName { get; internal set; } = fieldName;
    public int? HttpStatusCode { get; } = httpStatusCode;
    public string CauseType { get; } = causeType;
}

/// <summary>Read-only Prometheus range client for the frozen dashboard contract.</summary>
public sealed class GrafanaPrometheusStatisticsSource(
    HttpClient httpClient,
    IOptions<HistoricalStatisticsOptions> options) : IHistoricalStatisticsSource
{
    public async Task<StatisticsSourceResult> QueryAsync(DateTime fromMinuteUtc, DateTime toMinuteUtc, CancellationToken cancellationToken = default)
    {
        var currentOptions = options.Value;
        currentOptions.Validate();
        ValidateRange(fromMinuteUtc, toMinuteUtc);

        var values = new Dictionary<(string City, DateTime Minute), Dictionary<string, decimal>>(new StatisticKeyComparer());
        var warnings = new List<string>();
        var evaluationStart = new DateTimeOffset(fromMinuteUtc.AddMinutes(1), TimeSpan.Zero).ToUnixTimeSeconds();
        var evaluationEnd = new DateTimeOffset(toMinuteUtc.AddMinutes(1), TimeSpan.Zero).ToUnixTimeSeconds();

        foreach (var field in WorkerDashboardStatisticsCatalog.Fields)
        {
            string payload;
            try
            {
                var query = field.BuildQuery(currentOptions.Cities);
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(currentOptions.SourceEndpoint, query, evaluationStart, evaluationEnd));
                request.Headers.TryAddWithoutValidation("Authorization", currentOptions.ReaderAuthorization);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var code = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "metrics-source-authorization-failure",
                        HttpStatusCode.TooManyRequests => "metrics-source-rate-limited",
                        _ => "metrics-source-http-failure",
                    };
                    throw new StatisticsSourceException("Metrics source returned a non-success response.", code, field.FieldName, (int)response.StatusCode);
                }
                payload = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (StatisticsSourceException exception)
            {
                exception.FieldName = field.FieldName;
                throw;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new StatisticsSourceException("Metrics source request timed out.", "metrics-source-timeout", field.FieldName, causeType: exception.GetType().Name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Do not preserve the exception: HttpClient exceptions can contain the endpoint.
                throw new StatisticsSourceException("Metrics source request failed without exposing source configuration.", "metrics-source-transport-failure", field.FieldName, causeType: exception.GetType().Name);
            }

            try
            {
                ParseResponse(payload, field, currentOptions.Cities, fromMinuteUtc, toMinuteUtc, values, warnings);
            }
            catch (StatisticsSourceException exception)
            {
                exception.FieldName = field.FieldName;
                throw;
            }
        }

        var rows = new List<CityMinuteStatistic>();
        for (var minute = fromMinuteUtc; minute <= toMinuteUtc; minute = minute.AddMinutes(1))
        {
            foreach (var city in currentOptions.Cities)
            {
                values.TryGetValue((city, minute), out var fieldValues);
                fieldValues ??= [];
                var row = new CityMinuteStatistic
                {
                    CitySlug = city,
                    StatMinuteUtc = minute,
                    SourceDefinitionVersion = WorkerDashboardStatisticsCatalog.Version,
                    CollectionStatus = fieldValues.Count == 0
                        ? CollectionStatus.NoData
                        : fieldValues.Count == WorkerDashboardStatisticsCatalog.Fields.Count && warnings.Count == 0
                            ? CollectionStatus.Complete
                            : CollectionStatus.Partial,
                };

                foreach (var field in WorkerDashboardStatisticsCatalog.Fields)
                {
                    if (fieldValues.TryGetValue(field.FieldName, out var value))
                    {
                        try
                        {
                            SetValue(row, field, value);
                        }
                        catch (OverflowException)
                        {
                            throw new StatisticsSourceException("Metrics source value exceeded the destination field range.", "metrics-source-value-overflow", field.FieldName);
                        }
                    }
                }

                rows.Add(row);
            }
        }

        return new StatisticsSourceResult(rows, fromMinuteUtc, toMinuteUtc, warnings);
    }

    static Uri BuildRequestUri(string endpoint, string query, long start, long end)
    {
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return new Uri($"{endpoint}{separator}query={Uri.EscapeDataString(query)}&start={start.ToString(CultureInfo.InvariantCulture)}&end={end.ToString(CultureInfo.InvariantCulture)}&step=60", UriKind.Absolute);
    }

    static void ValidateRange(DateTime fromMinuteUtc, DateTime toMinuteUtc)
    {
        if (fromMinuteUtc.Kind != DateTimeKind.Utc || toMinuteUtc.Kind != DateTimeKind.Utc
            || fromMinuteUtc.Second != 0 || toMinuteUtc.Second != 0 || fromMinuteUtc > toMinuteUtc)
            throw new ArgumentException("Metrics source ranges must be aligned, ordered UTC minutes.");
    }

    static void ParseResponse(
        string payload,
        WorkerDashboardStatisticDefinition field,
        IReadOnlyCollection<string> configuredCities,
        DateTime fromMinuteUtc,
        DateTime toMinuteUtc,
        Dictionary<(string City, DateTime Minute), Dictionary<string, decimal>> values,
        List<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!string.Equals(root.GetProperty("status").GetString(), "success", StringComparison.Ordinal))
                throw new StatisticsSourceException("Metrics source returned an unsuccessful response.", "metrics-source-error-response");

            if (root.TryGetProperty("warnings", out var warningElement) && warningElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var warning in warningElement.EnumerateArray())
                    if (warning.ValueKind == JsonValueKind.String)
                        warnings.Add("Metrics source returned a warning.");
            }

            var data = root.GetProperty("data");
            if (!string.Equals(data.GetProperty("resultType").GetString(), "matrix", StringComparison.Ordinal))
                throw new StatisticsSourceException("Metrics source returned an unsupported result type.", "metrics-source-result-type");

            foreach (var series in data.GetProperty("result").EnumerateArray())
            {
                var metric = series.GetProperty("metric");
                if (!metric.TryGetProperty("transit_city", out var cityProperty) || cityProperty.ValueKind != JsonValueKind.String)
                    throw new StatisticsSourceException("Metrics source response omitted the transit_city label.", "metrics-source-city-label-missing");
                var city = cityProperty.GetString()!;
                if (!configuredCities.Contains(city, StringComparer.Ordinal))
                    throw new StatisticsSourceException("Metrics source returned an unexpected city label.", "metrics-source-city-label-unexpected");
                foreach (var sample in series.GetProperty("values").EnumerateArray())
                {
                    if (sample.ValueKind != JsonValueKind.Array || sample.GetArrayLength() != 2)
                        throw new StatisticsSourceException("Metrics source returned a malformed sample.", "metrics-source-malformed-sample");
                    var timestamp = ParseTimestamp(sample[0]);
                    var minute = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.AddMinutes(-1);
                    if (minute < fromMinuteUtc || minute > toMinuteUtc)
                        continue;
                    var value = ParseValue(sample[1], field.ValueKind);
                    var key = (city, minute);
                    if (!values.TryGetValue(key, out var rowValues))
                        values[key] = rowValues = new Dictionary<string, decimal>(StringComparer.Ordinal);
                    if (!rowValues.TryAdd(field.FieldName, value))
                        throw new StatisticsSourceException("Metrics source returned duplicate field samples.", "metrics-source-duplicate-sample");
                }
            }
        }
        catch (StatisticsSourceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // JSON and parsing errors must not include the response body or request URI.
            throw new StatisticsSourceException("Metrics source returned malformed data.", "metrics-source-malformed-response", causeType: exception.GetType().Name);
        }
    }

    static long ParseTimestamp(JsonElement element)
    {
        var value = ParseDecimal(element);
        if (value != decimal.Truncate(value))
            throw new StatisticsSourceException("Metrics source returned a non-integral timestamp.", "metrics-source-invalid-timestamp");
        return checked((long)value);
    }

    static decimal ParseValue(JsonElement element, StatisticsValueKind kind)
    {
        var value = ParseDecimal(element);
        if (value < 0 && kind != StatisticsValueKind.Decimal)
            throw new StatisticsSourceException("Metrics source returned a negative count.", "metrics-source-negative-count");
        if (kind is StatisticsValueKind.Integer or StatisticsValueKind.Ratio && value != decimal.Truncate(value))
            throw new StatisticsSourceException("Metrics source returned a non-integral value.", "metrics-source-nonintegral-value");
        if (kind == StatisticsValueKind.Ratio && value is not 0 and not 1)
            throw new StatisticsSourceException("Metrics source returned an invalid ratio.", "metrics-source-invalid-ratio");
        return value;
    }

    static decimal ParseDecimal(JsonElement element)
    {
        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0 && text?.StartsWith("-", StringComparison.Ordinal) == true)
            throw new StatisticsSourceException("Metrics source returned an invalid numeric value.", "metrics-source-invalid-number");
        return value;
    }

    static void SetValue(CityMinuteStatistic row, WorkerDashboardStatisticDefinition field, decimal value)
    {
        switch (field.FieldName)
        {
            case "last_cycled_unix_seconds": row.LastCycledUnixSeconds = checked((long)value); break;
            case "last_worked_unix_seconds": row.LastWorkedUnixSeconds = checked((long)value); break;
            case "cycle_rate_per_second": row.CycleRatePerSecond = value; break;
            case "cycle_error_rate_per_second": row.CycleErrorRatePerSecond = value; break;
            case "cycle_duration_p95_seconds": row.CycleDurationP95Seconds = value; break;
            case "healthy": row.Healthy = value == 1; break;
            case "input_fetch_ok": row.InputFetchOk = value == 1; break;
            case "input_records_valid": row.InputRecordsValid = checked((long)value); break;
            case "has_input_records": row.HasInputRecords = value == 1; break;
            case "input_lag_seconds": row.InputLagSeconds = value; break;
            case "input_timestamp_known": row.InputTimestampKnown = value == 1; break;
            case "input_source_failures": row.InputSourceFailures = checked((long)value); break;
            case "vehicles_processed": row.VehiclesProcessed = checked((long)value); break;
            case "tones_emitted": row.TonesEmitted = checked((long)value); break;
            case "batch_wire_bytes": row.BatchWireBytes = checked((long)value); break;
            case "crossings_suppressed_first_seen": row.CrossingsSuppressedFirstSeen = checked((long)value); break;
            case "crossings_suppressed_delta_leq_zero": row.CrossingsSuppressedDeltaLeqZero = checked((long)value); break;
            case "crossings_suppressed_teleport": row.CrossingsSuppressedTeleport = checked((long)value); break;
            case "crossings_suppressed_transfer": row.CrossingsSuppressedTransfer = checked((long)value); break;
            case "vehicle_state_cache": row.VehicleStateCache = checked((long)value); break;
            case "crossing_baseline_cache": row.CrossingBaselineCache = checked((long)value); break;
            case "route_index": row.RouteIndex = checked((long)value); break;
            case "route_trigger_point_cache": row.RouteTriggerPointCache = checked((long)value); break;
            default: throw new StatisticsSourceException("Metrics source field was not in the frozen catalogue.", "metrics-source-unknown-field");
        }
    }

    sealed class StatisticKeyComparer : IEqualityComparer<(string City, DateTime Minute)>
    {
        public bool Equals((string City, DateTime Minute) x, (string City, DateTime Minute) y) =>
            StringComparer.Ordinal.Equals(x.City, y.City) && x.Minute == y.Minute;
        public int GetHashCode((string City, DateTime Minute) obj) => HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.City), obj.Minute);
    }
}
