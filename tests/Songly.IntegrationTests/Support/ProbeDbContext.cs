using Microsoft.EntityFrameworkCore;

namespace Songly.IntegrationTests;

/// <summary>
/// A test-only view of the <c>users</c> table, for probes that need a real database constraint to
/// trip (unique e-mail) without depending on a module's internals.
/// </summary>
internal sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
{
    public DbSet<ProbeUser> Users => Set<ProbeUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ProbeUser>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
        });
}

internal sealed class ProbeUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = "not-a-real-hash";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
