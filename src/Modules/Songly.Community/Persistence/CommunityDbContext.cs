using Microsoft.EntityFrameworkCore;
using Songly.Common.Persistence;
using Songly.Community.Domain;

namespace Songly.Community.Persistence;

/// <summary>The community module's tables: <c>communities</c>, <c>community_members</c>, <c>community_bans</c>.</summary>
internal sealed class CommunityDbContext(DbContextOptions<CommunityDbContext> options) : DbContext(options)
{
    public DbSet<CommunityEntity> Communities => Set<CommunityEntity>();

    public DbSet<CommunityMember> Members => Set<CommunityMember>();

    public DbSet<CommunityBan> Bans => Set<CommunityBan>();

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

        modelBuilder.Entity<CommunityBan>(ban =>
        {
            ban.ToTable("community_bans");
            ban.HasKey(b => new { b.CommunityId, b.UserId });
            ban.Property(b => b.AppealStatus).HasConversion(new EnumNameConverter<BanAppealStatus>());
        });
    }
}
