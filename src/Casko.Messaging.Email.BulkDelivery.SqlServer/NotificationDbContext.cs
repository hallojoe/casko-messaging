using Microsoft.EntityFrameworkCore;

namespace Casko.Messaging.Email.BulkDelivery;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var events = modelBuilder.Entity<NotificationEvent>();
        events.ToTable("NotificationEvents");
        events.Property(x => x.EventType).HasMaxLength(200);
        events.Property(x => x.Template).HasMaxLength(200);
        events.Property(x => x.IdempotencyKey).HasMaxLength(200);
        events.HasIndex(x => x.IdempotencyKey).IsUnique();
        var deliveries = modelBuilder.Entity<NotificationDelivery>();
        deliveries.ToTable("NotificationDeliveries");
        deliveries.Property(x => x.EmailAddress).HasMaxLength(320);
        deliveries.Property(x => x.NormalizedEmailAddress).HasMaxLength(320);
        deliveries.Property(x => x.ProcessingWorkerId).HasMaxLength(200);
        deliveries.Property(x => x.SmtpMessageId).HasMaxLength(500);
        deliveries.Property(x => x.LastError).HasMaxLength(4000);
        deliveries.Property(x => x.RowVersion).IsRowVersion();
        deliveries.HasOne(x => x.NotificationEvent).WithMany(x => x.Deliveries).HasForeignKey(x => x.NotificationEventId).OnDelete(DeleteBehavior.Cascade);
        deliveries.HasIndex(x => new { x.NotificationEventId, x.NormalizedEmailAddress }).IsUnique();
        deliveries.HasIndex(x => new { x.Status, x.NextAttemptUtc });
        deliveries.HasIndex(x => new { x.Status, x.ProcessingLeaseUntilUtc });
        deliveries.HasIndex(x => new { x.Status, x.Priority, x.CreatedUtc, x.Id });
        deliveries.HasIndex(x => new { x.DeliveryBatchId, x.Status });
    }
}
