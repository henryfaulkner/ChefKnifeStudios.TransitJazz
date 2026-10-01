using System.Net;
using System.Net.Http;
using System.Text;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class GrafanaPrometheusStatisticsSourceTests
{
    [Fact]
    public async Task FixedResponsesUseLiteralQueriesAndPreserveClosedMinuteAlignment()
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler();
        var source = CreateSource(handler);

        var result = await source.QueryAsync(minute, minute);
        var row = Assert.Single(result.Rows);

        Assert.Equal(minute, row.StatMinuteUtc);
        Assert.Equal(CollectionStatus.Complete, row.CollectionStatus);
        Assert.True(row.Healthy);
        Assert.Equal(23, handler.Requests.Count);
        Assert.Contains(handler.Requests, request => request.Query.Contains("[1m]", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, request => request.Query.Contains("$__rate_interval", StringComparison.Ordinal));
        Assert.All(handler.Requests, request => Assert.Equal("Basic reader", request.Authorization));
        Assert.Contains("step=60", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZeroAndNullValuesRemainDistinctAndWarningsProducePartialRows()
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler
        {
            Warning = true,
            Value = "0",
        };
        var row = Assert.Single((await CreateSource(handler).QueryAsync(minute, minute)).Rows);

        Assert.Equal(CollectionStatus.Partial, row.CollectionStatus);
        Assert.Equal(0, row.Healthy is false ? 0 : 1);
        Assert.Equal(0, row.InputRecordsValid);
        Assert.Equal(0, row.VehiclesProcessed);

        var noData = Assert.Single((await CreateSource(new FixedPrometheusHandler { EmptyAll = true }).QueryAsync(minute, minute)).Rows);
        Assert.Equal(CollectionStatus.NoData, noData.CollectionStatus);
        Assert.Null(noData.InputRecordsValid);
    }

    [Fact]
    public async Task MissingCityErrorCounterSeriesLeavesItsRateNullAndMarksTheRowPartial()
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler
        {
            EmptyField = "transitjazz_worker_city_cycle_errors_total",
        };

        var row = Assert.Single((await CreateSource(handler).QueryAsync(minute, minute)).Rows);

        Assert.Equal(CollectionStatus.Partial, row.CollectionStatus);
        Assert.Null(row.CycleErrorRatePerSecond);
        Assert.True(row.Healthy);
    }

    [Fact]
    public async Task UndefinedP95LeavesTheValueNullAndMarksTheRowPartial()
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler
        {
            NaNField = "transitjazz_worker_city_cycle_duration_seconds_bucket",
        };

        var row = Assert.Single((await CreateSource(handler).QueryAsync(minute, minute)).Rows);

        Assert.Equal(CollectionStatus.Partial, row.CollectionStatus);
        Assert.Null(row.CycleDurationP95Seconds);
        Assert.True(row.Healthy);
        Assert.Equal(23, handler.Requests.Count);
    }

    [Fact]
    public async Task NaNInOtherFieldsStillFails()
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler
        {
            NaNField = "transitjazz_worker_city_last_cycled_seconds",
        };

        var exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));

        Assert.Equal("metrics-source-invalid-number", exception.Code);
        Assert.Equal("last_cycled_unix_seconds", exception.FieldName);
    }

    [Fact]
    public async Task SeparateInstancesInDifferentMinutesProduceOneRowPerMinute()
    {
        var firstMinute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler { SplitSeries = true };

        var rows = (await CreateSource(handler).QueryAsync(firstMinute, firstMinute.AddMinutes(1))).Rows;

        Assert.Collection(rows,
            first =>
            {
                Assert.Equal(firstMinute, first.StatMinuteUtc);
                Assert.Equal(CollectionStatus.Complete, first.CollectionStatus);
            },
            second =>
            {
                Assert.Equal(firstMinute.AddMinutes(1), second.StatMinuteUtc);
                Assert.Equal(CollectionStatus.Complete, second.CollectionStatus);
            });
    }

    [Theory]
    [InlineData("200", "100", 200L, 1L, true)]
    [InlineData("100", "200", 200L, 0L, false)]
    public async Task OverlappingInstancesUseTheNewestHeartbeatForEveryDirectField(
        string oldHeartbeat, string newHeartbeat, long expectedHeartbeat, long expectedVehicles, bool expectedHealthy)
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler
        {
            OverlappingSeries = true,
            OldHeartbeat = oldHeartbeat,
            NewHeartbeat = newHeartbeat,
        };

        var row = Assert.Single((await CreateSource(handler).QueryAsync(minute, minute)).Rows);

        Assert.Equal(CollectionStatus.Partial, row.CollectionStatus);
        Assert.Equal(expectedHeartbeat, row.LastCycledUnixSeconds);
        Assert.Equal(expectedVehicles, row.VehiclesProcessed);
        Assert.Equal(expectedHealthy, row.Healthy);
        Assert.Equal(1m, row.CycleDurationP95Seconds);
    }

    [Fact]
    public async Task TiedHeartbeatsDoNotSelectAnArbitraryInstance()
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler
        {
            OverlappingSeries = true,
            OldHeartbeat = "100",
            NewHeartbeat = "100",
        };

        var exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));

        Assert.Equal("metrics-source-ambiguous-instance", exception.Code);
        Assert.Equal("last_cycled_unix_seconds", exception.FieldName);
    }

    [Fact]
    public async Task UnexpectedLabelsMalformedDataAndDuplicateInstanceFailWithoutSecrets()
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler { City = "secret-city" };
        var exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));
        Assert.Equal("metrics-source-city-label-unexpected", exception.Code);
        Assert.Equal("last_cycled_unix_seconds", exception.FieldName);
        Assert.DoesNotContain("metrics.example", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reader", exception.Message, StringComparison.OrdinalIgnoreCase);

        handler = new FixedPrometheusHandler { Malformed = true };
        exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));
        Assert.Equal("metrics-source-malformed-response", exception.Code);
        Assert.Equal("last_cycled_unix_seconds", exception.FieldName);
        Assert.NotNull(exception.CauseType);
        Assert.DoesNotContain("metrics.example", exception.Message, StringComparison.OrdinalIgnoreCase);

        handler = new FixedPrometheusHandler { DuplicateSeries = true };
        exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));
        Assert.Equal("metrics-source-duplicate-sample", exception.Code);
        Assert.Equal("last_cycled_unix_seconds", exception.FieldName);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "metrics-source-authorization-failure")]
    [InlineData(HttpStatusCode.Forbidden, "metrics-source-authorization-failure")]
    [InlineData(HttpStatusCode.TooManyRequests, "metrics-source-rate-limited")]
    [InlineData(HttpStatusCode.InternalServerError, "metrics-source-http-failure")]
    public async Task HttpFailuresHaveSafeOperationalCodes(HttpStatusCode status, string code)
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler { ResponseStatus = status };

        var exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));

        Assert.Equal(code, exception.Code);
        Assert.Equal("last_cycled_unix_seconds", exception.FieldName);
        Assert.Equal((int)status, exception.HttpStatusCode);
        Assert.DoesNotContain("metrics.example", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, "metrics-source-transport-failure", "HttpRequestException")]
    [InlineData(true, "metrics-source-timeout", "TaskCanceledException")]
    public async Task RequestFailuresIdentifyTheFieldAndCauseWithoutExposingEndpoint(
        bool timedOut, string code, string causeType)
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        Exception cause = timedOut
            ? new TaskCanceledException("https://metrics.example/private?token=secret")
            : new HttpRequestException("https://metrics.example/private?token=secret");
        var handler = new FixedPrometheusHandler { Failure = cause };

        var exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));

        Assert.Equal(code, exception.Code);
        Assert.Equal("last_cycled_unix_seconds", exception.FieldName);
        Assert.Equal(causeType, exception.CauseType);
        Assert.DoesNotContain("metrics.example", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    static GrafanaPrometheusStatisticsSource CreateSource(FixedPrometheusHandler handler)
    {
        var options = new HistoricalStatisticsOptions
        {
            Enabled = true,
            SourceEndpoint = "https://metrics.example/api/v1/query_range",
            ReaderAuthorization = "Basic reader",
            Cities = ["atlanta"],
        };
        return new GrafanaPrometheusStatisticsSource(new HttpClient(handler), Options.Create(options));
    }

    sealed class FixedPrometheusHandler : HttpMessageHandler
    {
        public List<RequestCapture> Requests { get; } = [];
        public string? EmptyField { get; init; }
        public string City { get; init; } = "atlanta";
        public string Value { get; init; } = "1";
        public string? NaNField { get; init; }
        public bool Warning { get; init; }
        public bool EmptyAll { get; init; }
        public bool Malformed { get; init; }
        public bool DuplicateSeries { get; init; }
        public bool SplitSeries { get; init; }
        public bool OverlappingSeries { get; init; }
        public string OldHeartbeat { get; init; } = "100";
        public string NewHeartbeat { get; init; } = "200";
        public Exception? Failure { get; init; }
        public HttpStatusCode ResponseStatus { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query.Split("query=", StringSplitOptions.None)[1].Split('&')[0]);
            Requests.Add(new RequestCapture(request.RequestUri, query, request.Headers.Authorization?.ToString() ?? string.Empty));
            if (Failure is not null)
                throw Failure;
            if (ResponseStatus != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(ResponseStatus));
            if (Malformed)
                return Task.FromResult(Response("not-json"));
            if (EmptyAll || EmptyField is not null && query.Contains(EmptyField, StringComparison.Ordinal))
                return Task.FromResult(Response("{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[]}}"));
            var value = NaNField is not null && query.Contains(NaNField, StringComparison.Ordinal) ? "NaN" : Value;
            return Task.FromResult(Response(Json(query, value)));
        }

        string Json(string query, string value)
        {
            var timestamp = new DateTimeOffset(2026, 9, 20, 15, 5, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var histogram = query.Contains("histogram_quantile(", StringComparison.Ordinal);
            var heartbeat = query.Contains("transitjazz_worker_city_last_cycled_seconds", StringComparison.Ordinal);
            var oldValue = heartbeat && OverlappingSeries ? OldHeartbeat : value;
            var newValue = OverlappingSeries ? (heartbeat ? NewHeartbeat : "0") : value;
            var firstMetric = histogram
                ? $"{{\"transit_city\":\"{City}\"}}"
                : $"{{\"transit_city\":\"{City}\",\"instance\":\"old\"}}";
            var first = $"{{\"metric\":{firstMetric},\"values\":[[{timestamp},\"{oldValue}\"]]}}";
            var secondTimestamp = SplitSeries ? timestamp + 60 : timestamp;
            var secondInstance = DuplicateSeries ? "old" : "new";
            var secondMetric = histogram
                ? firstMetric
                : $"{{\"transit_city\":\"{City}\",\"instance\":\"{secondInstance}\"}}";
            var second = $"{{\"metric\":{secondMetric},\"values\":[[{secondTimestamp},\"{newValue}\"]]}}";
            var results = SplitSeries || (!histogram && (DuplicateSeries || OverlappingSeries)) ? $"[{first},{second}]" : $"[{first}]";
            var warnings = Warning ? ",\"warnings\":[\"partial\"]" : string.Empty;
            return $"{{\"status\":\"success\",\"data\":{{\"resultType\":\"matrix\",\"result\":{results}}}{warnings}}}";
        }

        static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
    }

    sealed record RequestCapture(Uri Uri, string Query, string Authorization);
}
