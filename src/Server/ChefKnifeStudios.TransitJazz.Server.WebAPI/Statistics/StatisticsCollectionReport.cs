using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public sealed record StatisticsCollectionReport(
    string SourceDefinitionVersion,
    bool DryRun,
    DateTime? RequestedStartUtc,
    DateTime? RequestedEndUtc,
    DateTime? ReturnedStartUtc,
    DateTime? ReturnedEndUtc,
    IReadOnlyCollection<string> Cities,
    int Created,
    int Unchanged,
    int Filled,
    int Discrepant,
    int CompleteRows,
    int PartialRows,
    int NoDataRows,
    IReadOnlyCollection<DateTime> GapsUtc,
    IReadOnlyCollection<string> Warnings,
    IReadOnlyCollection<string> Failures)
{
    public bool Succeeded => Failures.Count == 0 && Discrepant == 0;

    public static StatisticsCollectionReport Disabled(string version, IReadOnlyCollection<string> cities) => new(
        version, true, null, null, null, null, cities, 0, 0, 0, 0, 0, 0, 0, [], [], []);
}
