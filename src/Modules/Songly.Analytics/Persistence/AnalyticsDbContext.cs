using Microsoft.EntityFrameworkCore;
using Songly.Analytics.Domain;
using Songly.Common.Persistence;

namespace Songly.Analytics.Persistence;

/// <summary>The analytics module's table: <c>analytics_events</c>.</summary>
internal sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<AnalyticsEvent> Events => Set<AnalyticsEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalyticsEvent>(analyticsEvent =>
        {
            analyticsEvent.ToTable("analytics_events");
            analyticsEvent.HasKey(e => e.Id);
            analyticsEvent.Property(e => e.Type).HasConversion(new EnumNameConverter<AnalyticsEventType>());
        });
    }
}
