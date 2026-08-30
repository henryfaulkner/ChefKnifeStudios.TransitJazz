using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;

/// <summary>
/// Shares the loader's last successful static catalogue directly with the co-hosted worker.
/// Each caller observes a refresh published after the catalogue it last read, so a refresh
/// between the initial read and subscription cannot be missed.
/// </summary>
public sealed class InMemoryRouteShapeSource : IRouteShapeSource
{
    readonly object _gate = new();
    TaskCompletionSource<bool> _initialLoad = NewSignal();
    TaskCompletionSource<bool> _nextRefresh = NewSignal();
    IReadOnlyList<RouteShapeFeature> _shapes = [];
    long _generation;
    long _lastReadGeneration;

    public async Task<IReadOnlyList<RouteShapeFeature>> GetAllShapesAsync(CancellationToken ct)
    {
        Task initialLoad;
        lock (_gate)
        {
            if (_generation > 0)
            {
                _lastReadGeneration = _generation;
                return _shapes;
            }

            initialLoad = _initialLoad.Task;
        }

        await initialLoad.WaitAsync(ct);

        lock (_gate)
        {
            _lastReadGeneration = _generation;
            return _shapes;
        }
    }

    public async Task WaitForNextRefreshAsync(CancellationToken ct)
    {
        Task refresh;
        lock (_gate)
        {
            if (_generation > _lastReadGeneration)
            {
                _lastReadGeneration = _generation;
                return;
            }

            refresh = _nextRefresh.Task;
        }

        await refresh.WaitAsync(ct);

        lock (_gate)
            _lastReadGeneration = _generation;
    }

    /// <summary>Publishes a non-empty successful loader generation and wakes current waiters.</summary>
    public void Publish(IReadOnlyList<RouteShapeFeature> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        if (shapes.Count == 0)
            throw new ArgumentException("A route-shape generation cannot be empty.", nameof(shapes));

        lock (_gate)
        {
            _shapes = shapes.ToArray();
            _generation++;
            _initialLoad.TrySetResult(true);
            _nextRefresh.TrySetResult(true);
            _nextRefresh = NewSignal();
        }
    }

    static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
