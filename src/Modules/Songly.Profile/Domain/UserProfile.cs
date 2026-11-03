namespace Songly.Profile.Domain;

/// <summary>
/// The public part of an account, separate from its credentials on purpose (original Claude.md §5):
/// login data is sensitive, profile data is public. One per account (User 1—1 Profile).
/// </summary>
/// <remarks>
/// The original's <c>Profile</c>; renamed because a class called <c>Profile</c> inside the
/// <c>Songly.Profile</c> namespace would be ambiguous with the namespace in every file of the module.
/// </remarks>
internal sealed class UserProfile
{
    // For EF Core.
    private UserProfile() => DisplayName = string.Empty;

    public UserProfile(Guid userId, string displayName, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        DisplayName = displayName;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; }

    public string? Bio { get; private set; }

    /// <summary>
    /// The object storage key of the avatar, never a URL (original ADR 0010): the public address
    /// depends on the CDN's domain, and with keys a new domain is one setting, not an
    /// <c>UPDATE</c> of every table that ever stored an address.
    /// </summary>
    public string? AvatarKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Points the profile to another avatar file (or none). A separate operation from editing the
    /// profile because a file is involved: the caller has to know which key stopped being used, to
    /// delete it once the database no longer points to it.
    /// </summary>
    /// <returns>The previous key, if there was one and it changed.</returns>
    public string? ChangeAvatar(string? key, DateTimeOffset now)
    {
        var previous = AvatarKey;
        AvatarKey = key;
        UpdatedAt = now;
        return previous == key ? null : previous;
    }

    /// <summary>Replaces both fields: a bio left out of the request is a bio removed.</summary>
    public void Update(string displayName, string? bio, DateTimeOffset now)
    {
        DisplayName = displayName;
        Bio = bio;
        UpdatedAt = now;
    }
}
