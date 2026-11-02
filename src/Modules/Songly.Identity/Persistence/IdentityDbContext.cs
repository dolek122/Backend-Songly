using Microsoft.EntityFrameworkCore;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
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
    }
}
