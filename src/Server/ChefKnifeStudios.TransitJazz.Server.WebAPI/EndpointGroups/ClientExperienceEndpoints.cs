using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.Data.ClientExperience;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class ClientExperienceEndpoints
{
    static readonly JsonSerializerOptions StrictJson = CreateStrictJsonOptions();

    public static IEndpointRouteBuilder MapClientExperienceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ApiEndpoints.ClientExperience.Batch, HandleAsync)
            .WithName(nameof(ApiEndpoints.ClientExperience.Batch))
            .WithTags("ClientExperience")
            .AllowAnonymous()
            .Produces<ClientExperienceBatchAcknowledgement>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        endpoints.MapPost(ApiEndpoints.ClientExperience.SyntheticBatch, HandleSyntheticAsync)
            .WithName(nameof(ApiEndpoints.ClientExperience.SyntheticBatch))
            .WithTags("ClientExperience")
            .AllowAnonymous()
            .RequireRateLimiting("client-experience-synthetic")
            .Produces<ClientExperienceBatchAcknowledgement>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    static async Task<IResult> HandleAsync(
        HttpRequest request,
        IServiceProvider services,
        ClientExperienceOptions options,
        TimeProvider timeProvider,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
        => await HandleCoreAsync(request, services, options, timeProvider, environment.EnvironmentName,
            synthetic: false, cancellationToken: cancellationToken);

    static async Task<IResult> HandleSyntheticAsync(
        HttpRequest request,
        IServiceProvider services,
        ClientExperienceOptions options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!options.SyntheticIntakeEnabled || !options.Enabled)
        {
            ClientExperienceTelemetry.RecordRequest("synthetic_disabled", timer.Elapsed);
            return Problem("synthetic_intake_disabled", StatusCodes.Status503ServiceUnavailable);
        }
        if (!HasValidSyntheticCredential(request, options.SyntheticIntakeToken))
        {
            ClientExperienceTelemetry.RecordRequest("synthetic_unauthorized", timer.Elapsed);
            return Problem("synthetic_unauthorized", StatusCodes.Status401Unauthorized);
        }

        return await HandleCoreAsync(request, services, options, timeProvider, "synthetic",
            synthetic: true, cancellationToken: cancellationToken);
    }

    static bool HasValidSyntheticCredential(HttpRequest request, string configuredToken)
    {
        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (authorization.Length is < 39 or > 263
            || !authorization.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var suppliedToken = authorization[prefix.Length..];
        if (suppliedToken.Length is < 32 or > 256 || suppliedToken.Any(char.IsWhiteSpace))
            return false;

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredToken));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    static async Task<IResult> HandleCoreAsync(
        HttpRequest request,
        IServiceProvider services,
        ClientExperienceOptions options,
        TimeProvider timeProvider,
        string storedEnvironment,
        bool synthetic,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        var requestToken = timeout.Token;
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!options.Enabled)
        {
            ClientExperienceTelemetry.RecordRequest("disabled", timer.Elapsed);
            return Problem("intake_disabled", StatusCodes.Status503ServiceUnavailable);
        }
        if (!synthetic && !options.CaptureEnabled)
        {
            ClientExperienceTelemetry.RecordRequest("capture_disabled", timer.Elapsed);
            return Problem("capture_disabled", StatusCodes.Status503ServiceUnavailable);
        }
        if (request.ContentLength is > 0 && request.ContentLength > options.MaximumRequestBytes)
        {
            ClientExperienceTelemetry.RecordRequest("too_large", timer.Elapsed);
            return Problem("request_too_large", StatusCodes.Status413PayloadTooLarge);
        }
        if (!request.HasJsonContentType())
        {
            ClientExperienceTelemetry.RecordRequest("bad_content_type", timer.Elapsed);
            return Problem("invalid_envelope", StatusCodes.Status400BadRequest);
        }

        ClientExperienceBatch? batch;
        try
        {
            using var body = new MemoryStream(Math.Min(options.MaximumRequestBytes, 64 * 1024));
            var buffer = new byte[8192];
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer, requestToken);
                if (read == 0) break;
                if (body.Length + read > options.MaximumRequestBytes)
                {
                    ClientExperienceTelemetry.RecordRequest("too_large", timer.Elapsed);
                    return Problem("request_too_large", StatusCodes.Status413PayloadTooLarge);
                }
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            body.Position = 0;
            batch = await JsonSerializer.DeserializeAsync<ClientExperienceBatch>(body, StrictJson, requestToken);
        }
        catch (JsonException)
        {
            ClientExperienceTelemetry.RecordRequest("invalid_envelope", timer.Elapsed);
            return Problem("invalid_envelope", StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ClientExperienceTelemetry.RecordRequest("timeout", timer.Elapsed);
            return Problem("request_timeout", StatusCodes.Status503ServiceUnavailable);
        }

        if (batch is null || !ClientExperienceValidator.ValidateBatch(batch, options.MaximumEventsPerBatch, out _))
        {
            ClientExperienceTelemetry.RecordRequest("invalid_envelope", timer.Elapsed);
            return Problem("invalid_envelope", StatusCodes.Status400BadRequest);
        }

        foreach (var lost in batch.LostCounts ?? [])
            ClientExperienceTelemetry.RecordLoss(lost.Count, lost.EventKind, lost.Reason);

        var rejected = new List<ClientExperienceRejectedEvent>();
        var valid = new List<ValidatedClientExperienceEvent>(batch.Events.Count);
        var now = timeProvider.GetUtcNow();
        var allowedCities = options.AllowedCitySlugs.ToHashSet(StringComparer.Ordinal);
        foreach (var item in batch.Events)
        {
            var result = ClientExperienceValidator.Validate(item, now, allowedCities);
            if (result.IsValid) valid.Add(new(item, result.ClockSkewed));
            else rejected.Add(new(item.EventId, result.Reason));
        }

        if (valid.Count == 0)
        {
            ClientExperienceTelemetry.RecordEvents(0, 0, rejected.Count);
            ClientExperienceTelemetry.RecordRequest("ok", timer.Elapsed);
            return Results.Json(new ClientExperienceBatchAcknowledgement([], rejected), JsonSettings.DefaultOptions);
        }

        var store = services.GetService<IClientExperienceEventStore>();
        if (store is null)
        {
            ClientExperienceTelemetry.RecordRequest("persistence_unavailable", timer.Elapsed);
            return Problem("persistence_unavailable", StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var storedEvents = valid.Select(item =>
            {
                var source = item.Event;
                var storedEvent = new ChefKnifeStudios.TransitJazz.Server.Data.Models.ClientExperienceEvent
                {
                    EventId = source.EventId,
                    PageSessionId = source.PageSessionId,
                    CityActivationId = source.CityActivationId,
                    Sequence = source.Sequence,
                    OccurredAtUtc = source.OccurredAtUtc,
                    ReceivedAtUtc = now,
                    ClockSkewed = item.ClockSkewed,
                    Environment = storedEnvironment,
                    CitySlug = source.CitySlug,
                    ClientRelease = source.ClientRelease,
                    SchemaVersion = source.SchemaVersion,
                    MeasurementVersion = source.MeasurementVersion,
                    EventKind = source.EventKind,
                    PageElapsedMs = source.PageElapsedMs,
                    AttemptId = source.AttemptId,
                    EpisodeId = source.EpisodeId,
                    Journey = source.Journey,
                    Capability = source.Capability,
                    Outcome = source.Outcome,
                    Payload = JsonSerializer.Serialize(source, JsonSettings.DefaultOptions)
                };
                return new ClientExperienceStoredEvent(storedEvent, item.ClockSkewed);
            }).ToArray();
            var stored = await store.AppendAsync(storedEvents, storedEnvironment, now, requestToken);
            ClientExperienceTelemetry.RecordEvents(stored.Inserted, stored.Duplicates, rejected.Count);
            ClientExperienceTelemetry.RecordRequest("ok", timer.Elapsed);
            return Results.Json(new ClientExperienceBatchAcknowledgement(stored.AcknowledgedIds, rejected), JsonSettings.DefaultOptions);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ClientExperienceTelemetry.RecordRequest("timeout", timer.Elapsed);
            return Problem("persistence_unavailable", StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception)
        {
            ClientExperienceTelemetry.RecordRequest("persistence_unavailable", timer.Elapsed);
            return Problem("persistence_unavailable", StatusCodes.Status503ServiceUnavailable);
        }
    }

    static IResult Problem(string code, int statusCode)
        => Results.Problem(statusCode: statusCode, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    static JsonSerializerOptions CreateStrictJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSettings.DefaultOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        return options;
    }
}
