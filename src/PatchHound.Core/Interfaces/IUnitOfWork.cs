namespace PatchHound.Core.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);
    Task ExecuteResilientAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default
    );
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
