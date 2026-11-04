using Microsoft.EntityFrameworkCore;
using Songly.Content.Domain;

namespace Songly.Content.Persistence;

/// <summary>The content module's tables: <c>posts</c>, <c>comments</c>, <c>likes</c>, <c>shares</c>, <c>post_image_uploads</c>.</summary>
/// <remarks>
/// Comments, likes and shares reference the post with <c>ON DELETE CASCADE</c> — within this
/// module, so deleting a post takes everyone's activity under it. <c>posts.community_id</c> has no
/// cascade: that reference crosses into the community module, which tells content through
/// <c>CommunityDeleted</c> instead.
/// </remarks>
internal sealed class ContentDbContext(DbContextOptions<ContentDbContext> options) : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Like> Likes => Set<Like>();

    public DbSet<Share> Shares => Set<Share>();

    public DbSet<PostImageUpload> PostImageUploads => Set<PostImageUpload>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Post>(post =>
        {
            post.ToTable("posts");
            post.HasKey(p => p.Id);
        });

        modelBuilder.Entity<Comment>(comment =>
        {
            comment.ToTable("comments");
            comment.HasKey(c => c.Id);
        });

        modelBuilder.Entity<Like>(like =>
        {
            like.ToTable("likes");
            like.HasKey(l => new { l.PostId, l.UserId });
        });

        modelBuilder.Entity<Share>(share =>
        {
            share.ToTable("shares");
            share.HasKey(s => new { s.PostId, s.UserId });
        });

        modelBuilder.Entity<PostImageUpload>(upload =>
        {
            upload.ToTable("post_image_uploads");
            upload.HasKey(u => u.ImageKey);
        });
    }
}
