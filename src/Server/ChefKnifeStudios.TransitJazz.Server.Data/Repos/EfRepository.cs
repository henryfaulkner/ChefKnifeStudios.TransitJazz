using Ardalis.Specification.EntityFrameworkCore;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Repos;

public class EfRepository<T> : RepositoryBase<T>, IRepository<T>
    where T : class, IAggregateRoot
{
    public EfRepository(AppDbContext dbContext) : base(dbContext)
    {
    }
}

public class EfReadRepository<T> : RepositoryBase<T>, IReadRepository<T>
    where T : class, IReadAggregateRoot
{
    public EfReadRepository(AppDbContext dbContext) : base(dbContext)
    {
    }
}
