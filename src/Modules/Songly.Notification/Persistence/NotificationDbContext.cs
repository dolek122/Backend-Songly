using Microsoft.EntityFrameworkCore;
using Songly.Common.Persistence;
using Songly.Notification.Domain;

namespace Songly.Notification.Persistence;

/// <summary>The notification module's tables: <c>notifications</c>, <c>notification_mail_preferences</c>, <c>push_devices</c>.</summary>
internal sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    public DbSet<MailPreference> MailPreferences => Set<MailPreference>();

    public DbSet<PushDevice> PushDevices => Set<PushDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserNotification>(notification =>
        {
            notification.ToTable("notifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Type).HasConversion(new EnumNameConverter<NotificationType>());
        });

        modelBuilder.Entity<MailPreference>(preference =>
        {
            preference.ToTable("notification_mail_preferences");
            preference.HasKey(p => p.UserId);
        });

        modelBuilder.Entity<PushDevice>(device =>
        {
            device.ToTable("push_devices");
            device.HasKey(d => d.Token);
            device.Property(d => d.Platform).HasConversion(new EnumNameConverter<PushPlatform>());
        });
    }
}
