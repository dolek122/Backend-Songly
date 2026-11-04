using Microsoft.EntityFrameworkCore;
using Songly.NowPlaying.Domain;

namespace Songly.NowPlaying.Persistence;

/// <summary>
/// The now-playing module's tables: <c>now_playing_preferences</c>, <c>music_provider_links</c>. The statuses themselves are not
/// here — they live in Redis (see <c>StatusStore</c>).
/// </summary>
internal sealed class NowPlayingDbContext(DbContextOptions<NowPlayingDbContext> options) : DbContext(options)
{
    public DbSet<NowPlayingPreference> Preferences => Set<NowPlayingPreference>();

    public DbSet<MusicProviderLink> Links => Set<MusicProviderLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NowPlayingPreference>(preference =>
        {
            preference.ToTable("now_playing_preferences");
            preference.HasKey(p => p.UserId);
        });

        modelBuilder.Entity<MusicProviderLink>(link =>
        {
            link.ToTable("music_provider_links");
            link.HasKey(l => l.Id);
        });
    }
}
