using Microsoft.EntityFrameworkCore;
using Songly.Common.Persistence;
using Songly.Notification.Domain;

namespace Songly.Notification.Persistence;

/// <summary>The notification module's tables: <c>notifications</c>.</summary>
internal sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserNotification>(notification =>
        {
            notification.ToTable("notifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Type).HasConversion(new EnumNameConverter<NotificationType>());
        });
    }
}
