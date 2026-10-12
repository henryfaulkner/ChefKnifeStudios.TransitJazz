using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Shared;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public interface IHttpService
{
    Task<Result<T>> GetAsync<T>(string? requestUri, CancellationToken ct = default);
    Task<Result<T>> PostAsync<X, T>(string? requestUri, X body, CancellationToken ct = default);
    Task<HttpServiceResponse<T>> PostWithMetadataAsync<X, T>(string? requestUri, X body, CancellationToken ct = default);
    Task<Result> PostAsync<X>(string? requestUri, X body, CancellationToken ct = default);
    Task<Result<T>> PutAsync<X, T>(string? requestUri, X body, CancellationToken ct = default);
    Task<Result<T>> PatchAsync<X, T>(string? requestUri, X body, CancellationToken ct = default);
    Task<Result<T>> DeleteAsync<T>(string? requestUri, CancellationToken ct = default);
}

public sealed record HttpServiceResponse<T>(
    Result<T> Result,
    HttpStatusCode? StatusCode,
    string? ProblemCode,
    TimeSpan? RetryAfter);

public class HttpService : IHttpService
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _options;
    private readonly ILogger<HttpService> _logger;

    public HttpService(HttpClient client, ILogger<HttpService> logger)
    {
        _client = client;
        _logger = logger;
        _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        // Align with the server's serialization (JsonSettings.DefaultOptions) so polymorphic
        // EventEnvelope.Payload round-trips: registers EventEnvelopeConverter (+ enum converter,
        // camelCase naming, number handling). Without this the GetLastBatch warm-cache snapshot
        // fails to deserialize and the buses don't paint until the first SignalR batch arrives.
        JsonSettings.ApplyTo(_options);
    }

    public async Task<Result<T>> GetAsync<T>(string? requestUri, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetAsync(requestUri, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public async Task<Result<T>> PostAsync<X, T>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PostAsJsonAsync(requestUri, body, _options, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    /// <summary>Opt-in POST path for callers that need safe status and retry metadata.</summary>
    public async Task<HttpServiceResponse<T>> PostWithMetadataAsync<X, T>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(requestUri, body, _options, ct);
            var retryAfter = GetRetryAfter(response);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var value = JsonSerializer.Deserialize<T>(content, _options);
                if (value is null)
                {
                    return new HttpServiceResponse<T>(
                        Result<T>.Error("invalid_response"), response.StatusCode, "invalid_response", retryAfter);
                }
                return new HttpServiceResponse<T>(
                    Result<T>.Success(value!), response.StatusCode, null, retryAfter);
            }

            var code = await ReadSafeProblemCode(response, ct);
            var result = response.StatusCode switch
            {
                HttpStatusCode.NotFound => Result<T>.NotFound(),
                HttpStatusCode.Unauthorized => Result<T>.Unauthorized(),
                HttpStatusCode.Forbidden => Result<T>.Forbidden(),
                _ => Result<T>.Error(code ?? "request_failed")
            };
            return new HttpServiceResponse<T>(result, response.StatusCode, code, retryAfter);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return new HttpServiceResponse<T>(Result<T>.Error("invalid_response"), null, "invalid_response", null);
        }
        catch (Exception)
        {
            return new HttpServiceResponse<T>(Result<T>.Error("request_failed"), null, "request_failed", null);
        }
    }

    static readonly HashSet<string> SafeProblemCodes = new(StringComparer.Ordinal)
    {
        "stats_disabled", "dependency_unavailable", "invalid_request", "request_too_large",
        "unsupported_media_type", "invalid_scope", "invalid_city", "invalid_route_key",
        "scope_too_large", "response_too_large", "busy", "read_timeout", "rate_limited"
    };

    static async Task<string?> ReadSafeProblemCode(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("code", out var codeElement)
                || codeElement.ValueKind != JsonValueKind.String)
                return null;

            var code = codeElement.GetString();
            return code is not null && SafeProblemCodes.Contains(code) ? code : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retryAfter?.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
        return null;
    }

    public async Task<Result> PostAsync<X>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PostAsJsonAsync(requestUri, body, _options, ct);
            return response.IsSuccessStatusCode ? Result.Success() : Result.Error(response.ReasonPhrase ?? "Error");
        }
        catch (Exception ex)
        {
            return Result.Error(ex.Message);
        }
    }

    public async Task<Result<T>> PutAsync<X, T>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PutAsJsonAsync(requestUri, body, _options, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public async Task<Result<T>> PatchAsync<X, T>(string? requestUri, X body, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PatchAsJsonAsync(requestUri, body, _options, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public async Task<Result<T>> DeleteAsync<T>(string? requestUri, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.DeleteAsync(requestUri, ct);
            return await HandleResponse<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    private async Task<Result<T>> HandleResponse<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var raw = await response.Content.ReadAsStringAsync(ct);
                _logger.LogDebug("[HttpService] {Method} {Uri} → {Status} body_len={BodyLen}",
                    response.RequestMessage?.Method, response.RequestMessage?.RequestUri, (int)response.StatusCode, raw.Length);

                var content = JsonSerializer.Deserialize<T>(raw, _options);
                return Result<T>.Success(content!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HttpService] Deserialization failed for {Type}", typeof(T).Name);
                return Result<T>.Error($"Deserialization failed: {ex.Message}");
            }
        }

        var errorBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("[HttpService] {Method} {Uri} → {Status} body={Body}",
            response.RequestMessage?.Method, response.RequestMessage?.RequestUri, (int)response.StatusCode, errorBody);

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => Result<T>.NotFound(),
            System.Net.HttpStatusCode.Unauthorized => Result<T>.Unauthorized(),
            System.Net.HttpStatusCode.Forbidden => Result<T>.Forbidden(),
            _ => Result<T>.Error(response.ReasonPhrase ?? "Error")
        };
    }
}
