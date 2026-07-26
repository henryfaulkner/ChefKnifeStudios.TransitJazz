using Ardalis.Result;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;

public interface IKeyValueRepository<T>
{
    Task<Result<T>> GetAsync(string key, CancellationToken ct = default);
    Task<Result<Dictionary<string, T>>> GetAllAsync(CancellationToken ct = default);
    Task<Result> SetAsync(string key, T value, CancellationToken ct = default);
    Task<Result> DeleteAsync(string key, CancellationToken ct = default);
}
