using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.TelemetryData;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;

public interface ITelemetryEndpointsService
{
    Task<Result<TelemetryTodaySummaryDto>> GetTodaySummary(CancellationToken cancellationToken = default);
    Task<Result<TelemetryPageDto>> GetToday(string eventType, string? city, int page, int pageSize, TelemetrySortColumn sortBy, bool sortDesc, CancellationToken cancellationToken = default);
}

public class TelemetryEndpointsService : ITelemetryEndpointsService
{
    readonly ILogger<TelemetryEndpointsService> _logger;
    readonly IHttpService _httpService;

    public TelemetryEndpointsService(
        ILogger<TelemetryEndpointsService> logger,
        IHttpServiceFactory httpClientFactory)
    {
        _logger = logger;
        _httpService = httpClientFactory.Create(nameof(APIs.TransitJazzAPI));
    }

    public async Task<Result<TelemetryTodaySummaryDto>> GetTodaySummary(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _httpService.GetAsync<TelemetryTodaySummaryDto>(ApiEndpoints.Telemetry.GetTodaySummary, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetTodaySummary. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }

    public async Task<Result<TelemetryPageDto>> GetToday(string eventType, string? city, int page, int pageSize, TelemetrySortColumn sortBy, bool sortDesc, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{ApiEndpoints.Telemetry.GetToday}?eventType={Uri.EscapeDataString(eventType)}&page={page}&pageSize={pageSize}&sortBy={sortBy}&sortDesc={sortDesc}";
            if (city is not null)
                url += $"&city={Uri.EscapeDataString(city)}";

            var result = await _httpService.GetAsync<TelemetryPageDto>(url, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetToday. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }
}
