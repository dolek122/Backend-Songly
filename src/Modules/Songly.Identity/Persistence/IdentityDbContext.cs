using Microsoft.EntityFrameworkCore;
using Songly.Common.Persistence;
using Songly.Identity.Domain;

namespace Songly.Identity.Persistence;

/// <summary>
/// The identity module's view of the database: its own tables and nothing else. The schema
/// belongs to the migrations (ADR 0003); this only maps onto it, with names converted to
/// snake_case by convention.
/// </summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.SuspensionAppealStatus).HasConversion(new EnumNameConverter<SuspensionAppealStatus>());
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.ToTable("sessions");
            session.HasKey(s => s.Id);
        });

        modelBuilder.Entity<PendingRegistration>(pending =>
        {
            pending.ToTable("pending_registrations");
            pending.HasKey(p => p.Id);
        });

        modelBuilder.Entity<PasswordResetToken>(token =>
        {
            token.ToTable("password_reset_tokens");
            token.HasKey(t => t.Id);
        });
    }
}
