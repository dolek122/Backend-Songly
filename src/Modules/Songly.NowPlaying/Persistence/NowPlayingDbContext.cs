using Microsoft.EntityFrameworkCore;
using Songly.NowPlaying.Domain;

namespace Songly.NowPlaying.Persistence;

/// <summary>
/// The now-playing module's tables: <c>now_playing_preferences</c>. The statuses themselves are not
/// here — they live in Redis (see <c>StatusStore</c>).
/// </summary>
internal sealed class NowPlayingDbContext(DbContextOptions<NowPlayingDbContext> options) : DbContext(options)
{
    public DbSet<NowPlayingPreference> Preferences => Set<NowPlayingPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<NowPlayingPreference>(preference =>
        {
            preference.ToTable("now_playing_preferences");
            preference.HasKey(p => p.UserId);
        });
}
