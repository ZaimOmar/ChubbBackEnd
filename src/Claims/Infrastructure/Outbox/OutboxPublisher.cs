using System.Data.Common;
using Chubb.Claims.Application;
using Microsoft.Extensions.Options;

namespace Chubb.Claims.Infrastructure.Outbox;

/// <summary>
/// Publishes unpublished outbox rows in <c>sequence</c> order. Each cycle is one short transaction that holds the row
/// locks: fetch, send and mark row by row, stop at the first failure, commit marks and the failure together.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopes,
    IOutboxSender sender,
    OutboxPublisherState state,
    IOptions<OutboxPublisherOptions> options,
    TimeProvider time,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private readonly OutboxPublisherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = await RunCycleSafelyAsync(stoppingToken);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, time, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Outbox publisher stopping");
        }
    }

    private async Task<TimeSpan> RunCycleSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RunCycleAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            logger.LogWarning("Outbox store unavailable ({ErrorType}); retrying", exception.GetType().Name);
            return _options.BackoffCap;
        }
    }

    private async Task<TimeSpan> RunCycleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        await transaction.BeginAsync(cancellationToken);
        var batch = await store.FetchUnpublishedBatchAsync(_options.BatchSize, cancellationToken);
        var failure = default(OutboxFailure);
        var sent = 0;
        try
        {
            (sent, failure) = await SendInOrderAsync(batch, store, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Rows acknowledged before the stop are still marked; a duplicate would be harmless but avoidable.
            await CommitWithinGraceAsync(transaction);
            throw;
        }

        await CommitWithinGraceAsync(transaction);
        return NextDelay(batch.Count, sent, failure);
    }

    private async Task<(int Sent, OutboxFailure? Failure)> SendInOrderAsync(
        IReadOnlyList<OutboxRecord> batch, IOutboxStore store, CancellationToken cancellationToken)
    {
        var sent = 0;
        foreach (var record in batch)
        {
            try
            {
                await sender.SendAsync(record, cancellationToken);
            }
            catch (OutboxSendException exception)
            {
                return (sent, await RecordFailureAsync(record, exception, store));
            }

            await MarkPublishedAsync(record, store);
            sent++;
        }

        return (sent, null);
    }

    private async Task MarkPublishedAsync(OutboxRecord record, IOutboxStore store)
    {
        using var grace = new CancellationTokenSource(_options.ShutdownGrace);
        await store.MarkPublishedAsync(record.EventId, time.GetUtcNow(), grace.Token);
        if (state.Recover())
        {
            logger.LogInformation("OutboxRecovered at event {EventId}", record.EventId);
        }
    }

    private async Task<OutboxFailure> RecordFailureAsync(OutboxRecord record, OutboxSendException exception, IOutboxStore store)
    {
        using var grace = new CancellationTokenSource(_options.ShutdownGrace);
        await store.RecordFailureAsync(record.EventId, exception.Message, grace.Token);
        var attempts = record.Attempts + 1;
        if (!exception.IsRetryable || attempts >= _options.MaxAttempts)
        {
            ReportStuck(record, attempts, exception);
        }
        else
        {
            logger.LogWarning(
                "Outbox send failed for event {EventId} (sequence {Sequence}, attempt {Attempts}): {Error}",
                record.EventId, record.Sequence, attempts, exception.Message);
        }

        return new OutboxFailure(attempts);
    }

    private void ReportStuck(OutboxRecord record, int attempts, OutboxSendException exception)
    {
        if (state.MarkStuck(new StuckRow(record.EventId, record.Sequence, attempts)))
        {
            logger.LogError(
                "OutboxStuck at event {EventId} (sequence {Sequence}, attempts {Attempts}, retryable {IsRetryable}): {Error}",
                record.EventId, record.Sequence, attempts, exception.IsRetryable, exception.Message);
        }
    }

    private async Task CommitWithinGraceAsync(ICommandTransaction transaction)
    {
        using var grace = new CancellationTokenSource(_options.ShutdownGrace);
        await transaction.CommitAsync(grace.Token);
    }

    private TimeSpan NextDelay(int fetched, int sent, OutboxFailure? failure)
    {
        if (failure is not null)
        {
            return _options.BackoffFor(failure.Attempts);
        }

        return fetched > 0 && sent == _options.BatchSize ? TimeSpan.Zero : _options.PollInterval;
    }

    private sealed record OutboxFailure(int Attempts);
}
