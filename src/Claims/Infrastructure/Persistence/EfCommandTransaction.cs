using Chubb.Claims.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Chubb.Claims.Infrastructure.Persistence;

public sealed class EfCommandTransaction(ClaimsDbContext db) : ICommandTransaction, IAsyncDisposable
{
    private const string SavepointName = "command";

    private IDbContextTransaction? _transaction;

    private IDbContextTransaction Active => _transaction
        ?? throw new InvalidOperationException("No command transaction is active.");

    public async Task BeginAsync(CancellationToken cancellationToken) =>
        _transaction = await db.Database.BeginTransactionAsync(cancellationToken);

    public Task SavepointAsync(CancellationToken cancellationToken) =>
        Active.CreateSavepointAsync(SavepointName, cancellationToken);

    public async Task RollbackToSavepointAsync(CancellationToken cancellationToken)
    {
        await Active.RollbackToSavepointAsync(SavepointName, cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await Active.CommitAsync(cancellationToken);
        await EndAsync();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await EndAsync();
    }

    public ValueTask DisposeAsync() => _transaction?.DisposeAsync() ?? ValueTask.CompletedTask;

    private async Task EndAsync()
    {
        await Active.DisposeAsync();
        _transaction = null;
    }
}
