using Microsoft.EntityFrameworkCore;
using Songly.Profile.Domain;

namespace Songly.Profile.Persistence;

/// <summary>The profile module's tables: <c>profiles</c>, <c>genres</c>, <c>user_genres</c>.</summary>
internal sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> Profiles => Set<UserProfile>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<UserGenre> UserGenres => Set<UserGenre>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(profile =>
        {
            profile.ToTable("profiles");
            profile.HasKey(p => p.Id);
        });

        modelBuilder.Entity<Genre>(genre =>
        {
            genre.ToTable("genres");
            genre.HasKey(g => g.Id);
        });

        modelBuilder.Entity<UserGenre>(userGenre =>
        {
            userGenre.ToTable("user_genres");
            userGenre.HasKey(g => new { g.UserId, g.GenreId });
        });
    }
}
