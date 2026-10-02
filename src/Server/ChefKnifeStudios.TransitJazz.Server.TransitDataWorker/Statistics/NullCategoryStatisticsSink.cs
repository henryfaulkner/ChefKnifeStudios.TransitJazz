namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public sealed class NullCategoryStatisticsSink : ICategoryStatisticsSink
{
    public static NullCategoryStatisticsSink Instance { get; } = new();

    NullCategoryStatisticsSink() { }

    public bool TryEnqueue(FinalizedCategoryStatisticsBatch batch) => true;
}
