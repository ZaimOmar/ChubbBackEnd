using Chubb.Notification.Application;
using Chubb.Notification.Infrastructure.Messaging;
using Chubb.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Notification.Infrastructure;

public static class PersistenceSetup
{
    public const string ConnectionName = "NotificationDb";

    public static IServiceCollection AddNotificationPersistence(this IServiceCollection services)
    {
        services.AddDbContext<NotificationDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionName}' is not configured.");
            NotificationDbContextOptions.Configure(options, connectionString);
        });
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<InboxService>();
        return services;
    }

    /// <summary>
    /// Registers the Kafka consumer and its health check. Skipped when there is no database or no bootstrap servers
    /// (host tests); <see cref="StartNotificationStoreAsync"/> logs the warning.
    /// </summary>
    public static IServiceCollection AddClaimEventConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.IsConsumerConfigured())
        {
            return services;
        }

        services.AddOptions<KafkaConsumerOptions>()
            .BindConfiguration(KafkaConsumerOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.RetryBackoff >= TimeSpan.Zero && options.PollTimeout > TimeSpan.Zero, "Kafka timings must be positive.")
            .ValidateOnStart();
        services.AddSingleton<DeadLetterState>();
        services.AddSingleton<DeadLetterPublisher>();
        services.AddSingleton<INotificationDelivery, LogNotificationDelivery>();
        services.AddScoped<ClaimEventHandler>();
        services.AddHostedService<ClaimEventConsumer>();
        services.AddHealthChecks().AddCheck<DeadLetterHealthCheck>("dead-letters");
        return services;
    }

    // A host without a connection string (the host tests) starts without a database; any database use then fails loudly.
    public static async Task StartNotificationStoreAsync(this WebApplication app)
    {
        if (!app.Configuration.IsDatabaseConfigured())
        {
            app.Logger.LogWarning("Connection string {ConnectionName} is not configured; migrations and the consumer were skipped", ConnectionName);
            return;
        }

        if (!app.Configuration.IsConsumerConfigured())
        {
            app.Logger.LogWarning("{Key} is not configured; the consumer was skipped", KafkaConsumerOptions.BootstrapServersKey);
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await db.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
    }

    private static bool IsDatabaseConfigured(this IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionName));

    private static bool IsConsumerConfigured(this IConfiguration configuration) =>
        configuration.IsDatabaseConfigured() && !string.IsNullOrWhiteSpace(configuration[KafkaConsumerOptions.BootstrapServersKey]);
}
