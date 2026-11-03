using Microsoft.EntityFrameworkCore;
using Songly.Common.Persistence;
using Songly.Social.Domain;

namespace Songly.Social.Persistence;

/// <summary>The social module's tables: <c>friend_requests</c>, <c>friendships</c>, <c>blocks</c>.</summary>
internal sealed class SocialDbContext(DbContextOptions<SocialDbContext> options) : DbContext(options)
{
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Block> Blocks => Set<Block>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FriendRequest>(request =>
        {
            request.ToTable("friend_requests");
            request.HasKey(r => r.Id);
            request.Property(r => r.Status).HasConversion(new EnumNameConverter<FriendRequestStatus>());
        });

        modelBuilder.Entity<Friendship>(friendship =>
        {
            friendship.ToTable("friendships");
            friendship.HasKey(f => new { f.UserAId, f.UserBId });
        });

        modelBuilder.Entity<Block>(block =>
        {
            block.ToTable("blocks");
            block.HasKey(b => new { b.BlockerId, b.BlockedId });
        });
    }
}
