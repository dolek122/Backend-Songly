using Microsoft.EntityFrameworkCore;
using Songly.Common.Persistence;
using Songly.Community.Domain;

namespace Songly.Community.Persistence;

/// <summary>The community module's tables: <c>communities</c>, <c>community_members</c>.</summary>
internal sealed class CommunityDbContext(DbContextOptions<CommunityDbContext> options) : DbContext(options)
{
    public DbSet<CommunityEntity> Communities => Set<CommunityEntity>();

    public DbSet<CommunityMember> Members => Set<CommunityMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommunityEntity>(community =>
        {
            community.ToTable("communities");
            community.HasKey(c => c.Id);
        });

        modelBuilder.Entity<CommunityMember>(member =>
        {
            member.ToTable("community_members");
            member.HasKey(m => new { m.CommunityId, m.UserId });
            member.Property(m => m.Role).HasConversion(new EnumNameConverter<CommunityRole>());
        });
    }
}
