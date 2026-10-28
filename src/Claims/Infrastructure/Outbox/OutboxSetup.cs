using Chubb.Claims.Infrastructure.Outbox;

namespace Chubb.Claims.Infrastructure;

public static class OutboxSetup
{
    /// <summary>
    /// Registers the publisher, the producer and the <c>outbox</c> health check. Skipped, with a warning, when there is
    /// no Claims database (host tests) or <c>Outbox:Enabled</c> is false.
    /// </summary>
    public static IServiceCollection AddOutboxPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.IsClaimsDatabaseConfigured() || !configuration.GetValue(OutboxPublisherOptions.EnabledKey, true))
        {
            return services;
        }

        services.AddOptions<KafkaProducerOptions>()
            .BindConfiguration(KafkaProducerOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.MessageTimeout > TimeSpan.Zero && options.RequestTimeout > TimeSpan.Zero,
                "Kafka timeouts must be positive.")
            .ValidateOnStart();
        services.AddOptions<OutboxPublisherOptions>()
            .BindConfiguration(OutboxPublisherOptions.SectionName)
            .Validate(options => options.IsValid(), "Outbox options must be positive and BackoffCap >= BackoffInitial.")
            .ValidateOnStart();
        services.AddSingleton<OutboxPublisherState>();
        services.AddSingleton<IOutboxSender, KafkaOutboxSender>();
        services.AddHostedService<OutboxPublisher>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck>("outbox");
        return services;
    }
}
