using NurtricenterDeliverySchedule.Domain.Interfaces;
using NurtricenterDeliverySchedule.Infrastructure.Persistence.Repositories;

namespace NurtricenterDeliverySchedule.Infrastructure.Persistence;

public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];

    public IRepository<T> Repository<T>() where T : class
    {
        if (_repositories.TryGetValue(typeof(T), out var repo))
            return (IRepository<T>)repo;

        var repository = new Repository<T>(context);
        _repositories[typeof(T)] = repository;
        return repository;
    }

    public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => await context.SaveChangesAsync(cancellationToken);

    public void Dispose() => context.Dispose();
}
