using Microsoft.EntityFrameworkCore;
using Songly.Common.Persistence;
using Songly.ModerationLog.Domain;

namespace Songly.ModerationLog.Persistence;

/// <summary>The moderation log module's table: <c>moderation_log_entries</c>.</summary>
internal sealed class ModerationLogDbContext(DbContextOptions<ModerationLogDbContext> options) : DbContext(options)
{
    public DbSet<ModerationLogEntry> Entries => Set<ModerationLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModerationLogEntry>(entry =>
        {
            entry.ToTable("moderation_log_entries");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Action).HasConversion(new EnumNameConverter<ModerationAction>());
        });
    }
}
