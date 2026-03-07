using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Repositories;

public class RepositoryBase<T> : IRepository<T>
    where T : class
{
    protected readonly PatchHoundDbContext DbContext;
    protected readonly DbSet<T> DbSet;

    public RepositoryBase(PatchHoundDbContext dbContext)
    {
        DbContext = dbContext;
        DbSet = dbContext.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await DbSet.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        await DbSet.ToListAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct = default) =>
        await DbSet.AddAsync(entity, ct);

    public void Update(T entity) => DbSet.Update(entity);

    public void Remove(T entity) => DbSet.Remove(entity);
}
