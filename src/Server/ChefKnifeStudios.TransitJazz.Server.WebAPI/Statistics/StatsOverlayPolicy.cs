using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public sealed class StatsOverlayRequestException(string code, int statusCode) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record StatsResolvedCityWindow(
    StatsOverlayCityOptions City,
    DateOnly LocalDate,
    DateTime LocalDayStartUtc,
    DateTime LocalDayEndUtc,
    DateTime RequestedTodayStartUtc,
    DateTime RequestedTodayEndUtc,
    DateTime? EligibleTodayStartUtc,
    DateTime? EligibleTodayEndUtc,
    IReadOnlyList<DateTime> TodayHoursUtc,
    DateTime Last24StartUtc,
    DateTime Last24EndUtc,
    IReadOnlyList<DateTime> Last24HoursUtc,
    int BoundaryExcludedTodayHours);

public sealed record StatsSnapshotPlan(
    DateTime GeneratedAtUtc,
    DateTime CutoffUtc,
    StatsOverlayRequest Selection,
    IReadOnlyList<StatsResolvedCityWindow> Cities);

public static class StatsOverlayPolicy
{
    public static StatsOverlayRequest NormalizeRequest(
        StatsOverlayRequest? request,
        StatsOverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (request is null) throw new StatsOverlayRequestException("invalid_request", 400);
        if (request.RouteJoinKeys is null) throw new StatsOverlayRequestException("invalid_request", 400);

        if (string.Equals(request.Scope, "all", StringComparison.Ordinal))
        {
            if (request.CitySlug is not null || request.RouteJoinKeys.Length != 0)
                throw new StatsOverlayRequestException("invalid_scope", 400);
            return request with { Scope = "all", CitySlug = null, RouteJoinKeys = [] };
        }

        if (!string.Equals(request.Scope, "city", StringComparison.Ordinal))
            throw new StatsOverlayRequestException("invalid_scope", 400);
        if (request.CitySlug is null
            || !options.Cities.Any(city => string.Equals(city.CitySlug, request.CitySlug, StringComparison.Ordinal)))
            throw new StatsOverlayRequestException("invalid_city", 400);

        var normalizedKeys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in request.RouteJoinKeys)
        {
            if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) > options.MaximumRouteKeyBytes)
                throw new StatsOverlayRequestException("invalid_route_key", 400);
            normalizedKeys.Add(key);
            if (normalizedKeys.Count > options.MaximumSelectedRoutes)
                throw new StatsOverlayRequestException("request_too_large", 413);
        }

        return request with { Scope = "city", RouteJoinKeys = normalizedKeys.ToArray() };
    }

    public static StatsSnapshotPlan Resolve(
        StatsOverlayRequest normalizedRequest,
        StatsOverlayOptions options,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentNullException.ThrowIfNull(options);
        if (asOfUtc.Offset != TimeSpan.Zero)
            asOfUtc = asOfUtc.ToUniversalTime();
        var generatedAt = asOfUtc.UtcDateTime;
        var cutoff = FloorUtcHour(generatedAt.AddMinutes(-options.IngestionGraceMinutes));
        var selectedCities = string.Equals(normalizedRequest.Scope, "all", StringComparison.Ordinal)
            ? options.Cities
            : options.Cities.Where(city => string.Equals(city.CitySlug, normalizedRequest.CitySlug, StringComparison.Ordinal)).ToList();

        var windows = new List<StatsResolvedCityWindow>(selectedCities.Count);
        foreach (var city in selectedCities)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(generatedAt, zone));
            var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var nextLocalStart = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var dayStart = LocalBoundaryToUtc(localStart, zone, isStart: true);
            var dayEnd = LocalBoundaryToUtc(nextLocalStart, zone, isStart: false);
            var requestedEnd = cutoff < dayStart ? dayStart : Min(cutoff, dayEnd);
            var todayHours = FullUtcHours(dayStart, requestedEnd);
            DateTime? eligibleStart = todayHours.Count == 0 ? null : todayHours[0];
            DateTime? eligibleEnd = todayHours.Count == 0 ? null : todayHours[^1].AddHours(1);
            var boundaryCount = requestedEnd <= dayStart
                ? 0
                : (IsUtcHourBoundary(dayStart) ? 0 : 1) + (IsUtcHourBoundary(requestedEnd) ? 0 : 1);
            var last24Start = cutoff.AddHours(-24);
            var last24Hours = FullUtcHours(last24Start, cutoff);

            windows.Add(new StatsResolvedCityWindow(
                city,
                localDate,
                dayStart,
                dayEnd,
                dayStart,
                requestedEnd,
                eligibleStart,
                eligibleEnd,
                todayHours,
                last24Start,
                cutoff,
                last24Hours,
                boundaryCount));
        }

        return new StatsSnapshotPlan(generatedAt, cutoff, normalizedRequest, windows);
    }

    public static DateTime FloorUtcHour(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
    }

    static DateTime LocalBoundaryToUtc(DateTime localBoundary, TimeZoneInfo zone, bool isStart)
    {
        var adjusted = localBoundary;
        while (zone.IsInvalidTime(adjusted)) adjusted = adjusted.AddMinutes(1);
        if (!zone.IsAmbiguousTime(adjusted))
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(adjusted, zone), DateTimeKind.Utc);

        var offsets = zone.GetAmbiguousTimeOffsets(adjusted);
        var offset = isStart ? offsets.Max() : offsets.Min();
        return DateTime.SpecifyKind(adjusted - offset, DateTimeKind.Utc);
    }

    static IReadOnlyList<DateTime> FullUtcHours(DateTime startUtc, DateTime endUtc)
    {
        var hour = CeilingUtcHour(startUtc);
        var results = new List<DateTime>();
        while (hour.AddHours(1) <= endUtc)
        {
            results.Add(hour);
            hour = hour.AddHours(1);
        }
        return results;
    }

    static DateTime CeilingUtcHour(DateTime value)
    {
        var floor = FloorUtcHour(value);
        return floor == value ? floor : floor.AddHours(1);
    }

    static bool IsUtcHourBoundary(DateTime value) => FloorUtcHour(value) == value;
    static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
}
