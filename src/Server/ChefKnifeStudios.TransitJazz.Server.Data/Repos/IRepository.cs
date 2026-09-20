using Ardalis.Specification;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Repos;

public interface IRepository<T> : IRepositoryBase<T>
    where T : class, IAggregateRoot
{
}

public interface IReadRepository<T> : IReadRepositoryBase<T>
    where T : class, IReadAggregateRoot
{
}