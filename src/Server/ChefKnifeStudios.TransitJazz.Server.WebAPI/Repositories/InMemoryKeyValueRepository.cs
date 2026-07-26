using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Repositories;

public class InMemoryKeyValueRepository<T> : IKeyValueRepository<T>
{
    private readonly ConcurrentDictionary<string, T> _storage = new();

    public Task<Result<T>> GetAsync(string key, CancellationToken ct = default)
    {
        if (_storage.TryGetValue(key, out var value))
            return Task.FromResult(Result<T>.Success(value));
        return Task.FromResult(Result<T>.NotFound());
    }

    public Task<Result<Dictionary<string, T>>> GetAllAsync(CancellationToken ct = default)
    {
        var all = new Dictionary<string, T>(_storage);
        return Task.FromResult(Result<Dictionary<string, T>>.Success(all));
    }

    public Task<Result> SetAsync(string key, T value, CancellationToken ct = default)
    {
        _storage[key] = value;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> DeleteAsync(string key, CancellationToken ct = default)
    {
        if (_storage.TryRemove(key, out _))
            return Task.FromResult(Result.Success());
        return Task.FromResult(Result.NotFound());
    }
}
