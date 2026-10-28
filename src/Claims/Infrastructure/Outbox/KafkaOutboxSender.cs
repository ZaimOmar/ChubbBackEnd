using System.Globalization;
using System.Text;
using System.Text.Json;
using Chubb.Claims.Application;
using Chubb.Contracts;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Chubb.Claims.Infrastructure.Outbox;

public sealed class KafkaOutboxSender : IOutboxSender, IDisposable
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

    private static readonly HashSet<ErrorCode> NonRetryableErrors =
    [
        ErrorCode.Local_KeySerialization,
        ErrorCode.Local_ValueSerialization,
        ErrorCode.MsgSizeTooLarge,
        ErrorCode.Local_UnknownTopic,
        ErrorCode.UnknownTopicOrPart,
        ErrorCode.TopicAuthorizationFailed,
        ErrorCode.ClusterAuthorizationFailed,
    ];

    private readonly IProducer<string, string> _producer;

    public KafkaOutboxSender(IOptions<KafkaProducerOptions> options)
    {
        var settings = options.Value;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            ClientId = "claims-outbox",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = (int)settings.MessageTimeout.TotalMilliseconds,
            RequestTimeoutMs = (int)settings.RequestTimeout.TotalMilliseconds,
        }).Build();
    }

    public async Task SendAsync(OutboxRecord record, CancellationToken cancellationToken)
    {
        var message = new Message<string, string>
        {
            Key = record.ClaimId.ToString("D"),
            Value = record.Envelope,
            Headers = BuildHeaders(record),
        };
        try
        {
            await _producer.ProduceAsync(Topics.ClaimsEvents, message, cancellationToken);
        }
        catch (KafkaException exception)
        {
            var code = exception.Error.Code;
            throw new OutboxSendException(
                !NonRetryableErrors.Contains(code), $"{exception.GetType().Name}: {code}: {exception.Error.Reason}", exception);
        }
    }

    public void Dispose()
    {
        _producer.Flush(FlushTimeout);
        _producer.Dispose();
    }

    private static Headers BuildHeaders(OutboxRecord record) => new()
    {
        { HeaderNames.CorrelationId, Encode(ReadCorrelationId(record.Envelope)) },
        { KafkaHeaderNames.EventId, Encode(record.EventId.ToString("D")) },
        { KafkaHeaderNames.EventType, Encode(record.Type) },
        { KafkaHeaderNames.EventVersion, Encode(record.Version.ToString(CultureInfo.InvariantCulture)) },
    };

    private static string ReadCorrelationId(string envelope)
    {
        using var document = JsonDocument.Parse(envelope);
        if (document.RootElement.TryGetProperty("correlationId", out var value) && value.TryGetGuid(out var correlationId))
        {
            return correlationId.ToString("D");
        }

        throw new OutboxSendException(false, "MalformedEnvelope: correlationId is missing or not a GUID");
    }

    private static byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
}
