using System.Text;
using Chubb.Contracts;
using Chubb.Notification.Domain;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Chubb.Notification.Infrastructure.Messaging;

public sealed class ClaimEventConsumer(
    IServiceScopeFactory scopes,
    DeadLetterPublisher deadLetters,
    DeadLetterState deadLetterState,
    IOptions<KafkaConsumerOptions> options,
    ILogger<ClaimEventConsumer> logger) : BackgroundService
{
    private readonly KafkaConsumerOptions _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var consumer = BuildConsumer();
        consumer.Subscribe(Topics.ClaimsEvents);
        try
        {
            await ConsumeAsync(consumer, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Claim event consumer is stopping");
        }
        finally
        {
            consumer.Close();
        }
    }

    private IConsumer<string, string> BuildConsumer() =>
        new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.GroupId,
            ClientId = "notification-consumer",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();

    private async Task ConsumeAsync(IConsumer<string, string> consumer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = Poll(consumer);
            if (result is null)
            {
                continue;
            }

            await ProcessAsync(result, cancellationToken);
            consumer.Commit(result);
        }
    }

    private ConsumeResult<string, string>? Poll(IConsumer<string, string> consumer)
    {
        try
        {
            return consumer.Consume(_settings.PollTimeout);
        }
        catch (ConsumeException exception)
        {
            logger.LogWarning("Polling {Topic} failed with {ErrorCode}", Topics.ClaimsEvents, exception.Error.Code);
            return null;
        }
    }

    private async Task ProcessAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        if (TryFindHeaderMismatch(result) is { } mismatch)
        {
            await DeadLetterAsync(result, mismatch, cancellationToken);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            var failure = await TryHandleAsync(result, cancellationToken);
            if (failure is null)
            {
                return;
            }

            logger.LogWarning("Attempt {Attempt} of {MaxAttempts} failed with {ErrorType}", attempt, _settings.MaxAttempts, failure.GetType().Name);
            if (attempt >= _settings.MaxAttempts)
            {
                await DeadLetterAsync(result, failure, cancellationToken);
                return;
            }

            await Task.Delay(_settings.RetryBackoff * attempt, cancellationToken);
        }
    }

    // Not retried: the same message would fail the same way.
    private static InvalidEventException? TryFindHeaderMismatch(ConsumeResult<string, string> result)
    {
        try
        {
            EnvelopeHeaderCheck.EnsureMatches(result.Message.Headers, result.Message.Value);
            return null;
        }
        catch (InvalidEventException exception)
        {
            return exception;
        }
    }

    private async Task<Exception?> TryHandleAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ClaimEventHandler>().HandleAsync(result.Message.Value, cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception;
        }
    }

    private async Task DeadLetterAsync(ConsumeResult<string, string> result, Exception error, CancellationToken cancellationToken)
    {
        await deadLetters.PublishAsync(result, error, _settings.MaxAttempts, cancellationToken);
        deadLetterState.Record();
        logger.LogError(
            "EventDeadLettered consumer {Consumer} topic {Topic} partition {Partition} offset {Offset} eventId {EventId} type {EventType} error {ErrorType}",
            _settings.GroupId, result.Topic, result.Partition.Value, result.Offset.Value,
            HeaderValue(result, KafkaHeaderNames.EventId), HeaderValue(result, KafkaHeaderNames.EventType), error.GetType().Name);
    }

    private static string HeaderValue(ConsumeResult<string, string> result, string name) =>
        result.Message.Headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : "unknown";
}
