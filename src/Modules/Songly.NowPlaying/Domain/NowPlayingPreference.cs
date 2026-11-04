namespace Songly.NowPlaying.Domain;

/// <summary>
/// Whether a user's status may be shown to friends (original ADR 0007, stage 4). The row is written
/// only on the first change — no row means "visible", so nobody needs a record at registration.
/// </summary>
internal sealed class NowPlayingPreference(Guid userId, bool shareWithFriends, DateTimeOffset updatedAt)
{
    public Guid UserId { get; private set; } = userId;

    public bool ShareWithFriends { get; private set; } = shareWithFriends;

    public DateTimeOffset UpdatedAt { get; private set; } = updatedAt;

    public void Update(bool shareWithFriends, DateTimeOffset now)
    {
        ShareWithFriends = shareWithFriends;
        UpdatedAt = now;
    }
}
