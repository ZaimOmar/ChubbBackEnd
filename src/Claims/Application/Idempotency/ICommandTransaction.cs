namespace Chubb.Claims.Application;

/// <summary>The only owner of BEGIN, SAVEPOINT and COMMIT for a request; handlers never call it.</summary>
public interface ICommandTransaction
{
    Task BeginAsync(CancellationToken cancellationToken);

    Task SavepointAsync(CancellationToken cancellationToken);

    /// <summary>Rolls back to the savepoint and forgets every tracked entity.</summary>
    Task RollbackToSavepointAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Ends an uncommitted transaction, releasing its locks at once; does nothing after a commit.</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}
