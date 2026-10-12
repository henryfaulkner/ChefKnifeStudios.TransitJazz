#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost(ApiEndpoints.Stats.Query, HandleAsync)
            .WithName(nameof(ApiEndpoints.Stats.Query))
            .WithTags(nameof(ApiEndpoints.Stats))
            .AllowAnonymous()
            .RequireRateLimiting("StatsOverlay")
            .Produces<StatsOverlayResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413RequestEntityTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return builder;
    }

    static async Task<IResult> HandleAsync(
        HttpRequest request,
        StatsOverlayService service,
        StatsOverlayOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        long requestBytes = 0;
        var scope = "unknown";
        IResult Finish(IResult result, int statusCode, string? code = null)
        {
            StatsOverlayTelemetry.RecordEndpointRequest(scope,
                statusCode.ToString(CultureInfo.InvariantCulture), code, stopwatch.Elapsed, requestBytes);
            return result;
        }

        request.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!options.Enabled) return Finish(Problem("stats_disabled", StatusCodes.Status503ServiceUnavailable),
            StatusCodes.Status503ServiceUnavailable, "stats_disabled");
        if (request.ContentLength is > 0 && request.ContentLength > options.MaximumRequestBytes)
        {
            requestBytes = request.ContentLength.Value;
            return Finish(Problem("request_too_large", StatusCodes.Status413RequestEntityTooLarge),
                StatusCodes.Status413RequestEntityTooLarge, "request_too_large");
        }
        if (!request.HasJsonContentType())
            return Finish(Problem("unsupported_media_type", StatusCodes.Status415UnsupportedMediaType),
                StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type");

        StatsOverlayRequest? query;
        try
        {
            using var body = new MemoryStream(Math.Min(options.MaximumRequestBytes, 64 * 1024));
            var buffer = new byte[32 * 1024];
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                requestBytes += read;
                if (body.Length + read > options.MaximumRequestBytes)
                    return Finish(Problem("request_too_large", StatusCodes.Status413RequestEntityTooLarge),
                        StatusCodes.Status413RequestEntityTooLarge, "request_too_large");
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            body.Position = 0;
            query = await JsonSerializer.DeserializeAsync<StatsOverlayRequest>(body, JsonSettings.DefaultOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatsOverlayTelemetry.RecordEndpointRequest("unknown", "cancelled", "cancelled", stopwatch.Elapsed, requestBytes);
            throw;
        }
        catch (JsonException)
        {
            return Finish(Problem("invalid_request", StatusCodes.Status400BadRequest),
                StatusCodes.Status400BadRequest, "invalid_request");
        }

        scope = query?.Scope switch { "city" => "city", "all" => "all", _ => "unknown" };
        try
        {
            var response = await service.ReadAsync(query, cancellationToken);
            return Finish(Results.Json(response, JsonSettings.DefaultOptions, statusCode: StatusCodes.Status200OK),
                StatusCodes.Status200OK);
        }
        catch (StatsOverlayRequestException exception)
        {
            return Finish(Problem(exception.Code, exception.StatusCode), exception.StatusCode, exception.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatsOverlayTelemetry.RecordEndpointRequest(scope, "cancelled", "cancelled", stopwatch.Elapsed, requestBytes);
            throw;
        }
        catch (Exception)
        {
            return Finish(Problem("dependency_unavailable", StatusCodes.Status503ServiceUnavailable),
                StatusCodes.Status503ServiceUnavailable, "dependency_unavailable");
        }
    }

    static IResult Problem(string code, int statusCode) => Results.Problem(
        statusCode: statusCode,
        title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
