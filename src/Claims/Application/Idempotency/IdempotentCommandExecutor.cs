using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed class IdempotentCommandExecutor(
    ICommandTransaction transaction,
    IIdempotencyStore store,
    IRefusedAttemptRecorder refusedRecorder)
{
    private const int SuccessFloor = 200;
    private const int SuccessCeiling = 299;

    public async Task<IdempotencyOutcome> ExecuteAsync(
        IdempotencyRequest request,
        Func<CancellationToken, Task<StoredResponse>> run,
        Func<DomainRefusal, StoredResponse> refusalToResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.BeginAsync(cancellationToken);
            return await ExecuteInTransactionAsync(request, run, refusalToResponse, cancellationToken);
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task<IdempotencyOutcome> ExecuteInTransactionAsync(
        IdempotencyRequest request,
        Func<CancellationToken, Task<StoredResponse>> run,
        Func<DomainRefusal, StoredResponse> refusalToResponse,
        CancellationToken cancellationToken)
    {
        if (!await store.TryLockAsync(request, cancellationToken))
        {
            return new IdempotencyOutcome.InProgress();
        }

        if (await store.FindAsync(request, cancellationToken) is { } existing)
        {
            return existing.RequestHash.AsSpan().SequenceEqual(request.RequestHash)
                ? new IdempotencyOutcome.Replayed(existing.Response)
                : new IdempotencyOutcome.KeyReused();
        }

        await transaction.SavepointAsync(cancellationToken);
        return await RunCommandAsync(request, run, refusalToResponse, cancellationToken);
    }

    private async Task<IdempotencyOutcome> RunCommandAsync(
        IdempotencyRequest request,
        Func<CancellationToken, Task<StoredResponse>> run,
        Func<DomainRefusal, StoredResponse> refusalToResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await run(cancellationToken);
            return IsSuccess(response)
                ? await StoreAndCommitAsync(request, response, cancellationToken)
                : new IdempotencyOutcome.NotStored(response);
        }
        catch (DomainRefusalException refusal) when (refusal.Refusal.IsTrailed)
        {
            return await RecordRefusalAsync(request, refusal, refusalToResponse(refusal.Refusal), cancellationToken);
        }
        catch (DomainRefusalException refusal)
        {
            return new IdempotencyOutcome.NotStored(refusalToResponse(refusal.Refusal));
        }
    }

    // After a failed SaveChanges the transaction is aborted, so only the savepoint rollback is valid before the entry is written.
    private async Task<IdempotencyOutcome> RecordRefusalAsync(
        IdempotencyRequest request, DomainRefusalException refusal, StoredResponse response, CancellationToken cancellationToken)
    {
        await transaction.RollbackToSavepointAsync(cancellationToken);
        await refusedRecorder.RecordAsync(refusal, cancellationToken);
        return await StoreAndCommitAsync(request, response, cancellationToken);
    }

    private async Task<IdempotencyOutcome> StoreAndCommitAsync(
        IdempotencyRequest request, StoredResponse response, CancellationToken cancellationToken)
    {
        await store.AddAsync(request, response, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IdempotencyOutcome.Executed(response);
    }

    private static bool IsSuccess(StoredResponse response) => response.StatusCode is >= SuccessFloor and <= SuccessCeiling;
}
