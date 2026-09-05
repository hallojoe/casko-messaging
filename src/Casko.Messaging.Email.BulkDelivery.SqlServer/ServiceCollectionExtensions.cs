using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Casko.Messaging.Email.BulkDelivery;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqlServerNotifications(this IServiceCollection services, string? connectionString)
    {
        services.AddOptions<NotificationIngestionOptions>()
            .Validate(o => o.MaximumEvents > 0 && o.MaximumRecipients > 0 && o.MaximumRequestBytes > 0, "Ingestion limits must be positive.")
            .ValidateOnStart();
        services.AddDbContext<NotificationDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<INotificationWriter, SqlServerNotificationWriter>();
        services.AddScoped<INotificationQueueStore, SqlServerNotificationQueueStore>();
        services.AddScoped<INotificationStoreInitializer, SqlServerNotificationStoreInitializer>();
        return services;
    }
}

internal sealed class SqlServerNotificationStoreInitializer(NotificationDbContext db) : INotificationStoreInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => db.Database.MigrateAsync(cancellationToken);
}
