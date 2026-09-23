using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Statistics;

public interface ICityMinuteStatisticsStore
{
    Task<StatisticsWriteReport> UpsertAsync(IReadOnlyCollection<CityMinuteStatistic> rows, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CityMinuteStatistic>> ReadAsync(string citySlug, DateTime fromUtcInclusive, DateTime toUtcExclusive, CancellationToken cancellationToken = default);
}

public sealed record StatisticsWriteReport(int Created, int Unchanged, int Filled, int Discrepant)
{
    public int Total => Created + Unchanged + Filled + Discrepant;
}

/// <summary>Owns bounded city-minute persistence and never overwrites confirmed source values.</summary>
public sealed class CityMinuteStatisticsStore(IDbContextFactory<AppDbContext> contextFactory) : ICityMinuteStatisticsStore
{
    public async Task<StatisticsWriteReport> UpsertAsync(IReadOnlyCollection<CityMinuteStatistic> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return new StatisticsWriteReport(0, 0, 0, 0);
        if (rows.Count > 2_000)
            throw new ArgumentOutOfRangeException(nameof(rows), "Statistics batches may contain at most 2,000 rows.");

        foreach (var row in rows)
            row.Validate();

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var citySlugs = rows.Select(row => row.CitySlug).Distinct(StringComparer.Ordinal).ToArray();
        var minMinute = rows.Min(row => row.StatMinuteUtc);
        var maxMinute = rows.Max(row => row.StatMinuteUtc);
        var existing = await db.CityMinuteStatistics
            .Where(row => citySlugs.Contains(row.CitySlug) && row.StatMinuteUtc >= minMinute && row.StatMinuteUtc <= maxMinute)
            .ToDictionaryAsync(row => (row.CitySlug, row.StatMinuteUtc), cancellationToken);

        var created = 0;
        var unchanged = 0;
        var filled = 0;
        var discrepant = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var incoming in rows)
        {
            var key = (incoming.CitySlug, incoming.StatMinuteUtc);
            if (!existing.TryGetValue(key, out var current))
            {
                db.CityMinuteStatistics.Add(incoming.Clone());
                created++;
                continue;
            }

            if (!current.HasCompatibleSourceValues(incoming))
            {
                current.CollectionStatus = CollectionStatus.Discrepant;
                discrepant++;
                continue;
            }

            if (current.SourceDefinitionVersion == incoming.SourceDefinitionVersion
                && current.HasSameSourceValues(incoming))
            {
                unchanged++;
                continue;
            }

            var changed = current.FillMissingSourceValuesFrom(incoming);
            if (changed > 0)
                filled++;
            else
                unchanged++;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException("Statistics persistence encountered a city-minute key conflict; no confirmed value was overwritten.");
        }

        return new StatisticsWriteReport(created, unchanged, filled, discrepant);
    }

    public async Task<IReadOnlyList<CityMinuteStatistic>> ReadAsync(
        string citySlug,
        DateTime fromUtcInclusive,
        DateTime toUtcExclusive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(citySlug);
        if (fromUtcInclusive.Kind != DateTimeKind.Utc || toUtcExclusive.Kind != DateTimeKind.Utc || fromUtcInclusive >= toUtcExclusive)
            throw new ArgumentException("Statistics ranges must be non-empty UTC ranges.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CityMinuteStatistics
            .AsNoTracking()
            .Where(row => row.CitySlug == citySlug
                && row.StatMinuteUtc >= fromUtcInclusive
                && row.StatMinuteUtc < toUtcExclusive)
            .OrderBy(row => row.StatMinuteUtc)
            .ToListAsync(cancellationToken);
    }
}
