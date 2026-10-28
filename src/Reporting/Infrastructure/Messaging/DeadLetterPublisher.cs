using System.Globalization;
using System.Text;
using Chubb.Contracts;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Chubb.Reporting.Infrastructure.Messaging;

public sealed class DeadLetterPublisher : IDisposable
{
    public const string ConsumerHeader = "dlq-consumer";
    public const string ErrorTypeHeader = "dlq-error-type";
    public const string AttemptsHeader = "dlq-attempts";

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

    private readonly IProducer<string, string> _producer;
    private readonly string _consumerName;

    public DeadLetterPublisher(IOptions<KafkaConsumerOptions> options)
    {
        _consumerName = options.Value.GroupId;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = "reporting-dlq",
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();
    }

    // The original headers travel with the message; only the error class is added, never its text, which may echo data.
    public async Task PublishAsync(ConsumeResult<string, string> failed, Exception error, int attempts, CancellationToken cancellationToken)
    {
        var headers = new Headers();
        foreach (var header in failed.Message.Headers)
        {
            headers.Add(header.Key, header.GetValueBytes());
        }

        headers.Add(ConsumerHeader, Encode(_consumerName));
        headers.Add(ErrorTypeHeader, Encode(error.GetType().Name));
        headers.Add(AttemptsHeader, Encode(attempts.ToString(CultureInfo.InvariantCulture)));
        var message = new Message<string, string> { Key = failed.Message.Key, Value = failed.Message.Value, Headers = headers };
        await _producer.ProduceAsync(Topics.ClaimsEventsDlq, message, cancellationToken);
    }

    public void Dispose()
    {
        _producer.Flush(FlushTimeout);
        _producer.Dispose();
    }

    private static byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
}
