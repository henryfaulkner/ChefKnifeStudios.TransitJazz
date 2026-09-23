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
    public async Task UnexpectedLabelsMalformedDataAndDuplicateCardinalityFailWithoutSecrets()
    {
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var handler = new FixedPrometheusHandler { City = "secret-city" };
        var exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));
        Assert.DoesNotContain("metrics.example", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reader", exception.Message, StringComparison.OrdinalIgnoreCase);

        handler = new FixedPrometheusHandler { Malformed = true };
        exception = await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));
        Assert.DoesNotContain("metrics.example", exception.Message, StringComparison.OrdinalIgnoreCase);

        handler = new FixedPrometheusHandler { DuplicateSeries = true };
        await Assert.ThrowsAsync<StatisticsSourceException>(() => CreateSource(handler).QueryAsync(minute, minute));
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
        public bool Warning { get; init; }
        public bool EmptyAll { get; init; }
        public bool Malformed { get; init; }
        public bool DuplicateSeries { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query.Split("query=", StringSplitOptions.None)[1].Split('&')[0]);
            Requests.Add(new RequestCapture(request.RequestUri, query, request.Headers.Authorization?.ToString() ?? string.Empty));
            if (Malformed)
                return Task.FromResult(Response("not-json"));
            if (EmptyAll || EmptyField is not null && query.Contains(EmptyField, StringComparison.Ordinal))
                return Task.FromResult(Response("{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[]}}"));
            return Task.FromResult(Response(Json(Warning, DuplicateSeries)));
        }

        string Json(bool warning, bool duplicate)
        {
            var timestamp = new DateTimeOffset(2026, 9, 20, 15, 5, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var series = $"{{\"metric\":{{\"transit_city\":\"{City}\"}},\"values\":[[{timestamp},\"{Value}\"]]}}";
            var results = duplicate ? $"[{series},{series}]" : $"[{series}]";
            var warnings = warning ? ",\"warnings\":[\"partial\"]" : string.Empty;
            return $"{{\"status\":\"success\",\"data\":{{\"resultType\":\"matrix\",\"result\":{results}}}{warnings}}}";
        }

        static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
    }

    sealed record RequestCapture(Uri Uri, string Query, string Authorization);
}
