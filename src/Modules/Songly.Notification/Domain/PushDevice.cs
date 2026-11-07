namespace Songly.Notification.Domain;

/// <summary>Informative today — FCM serves both through one API.</summary>
internal enum PushPlatform
{
    Android,
    Ios,
}

/// <summary>
/// A device that gets pushes (<c>push_devices</c>, original ADR 0040), keyed by its FCM token: the token
/// identifies an installation of the app, and one phone has one user signed in at a time.
/// </summary>
internal sealed class PushDevice
{
    // For EF Core.
    private PushDevice()
    {
        Token = string.Empty;
    }

    public PushDevice(string token, Guid userId, PushPlatform platform, DateTimeOffset now)
    {
        Token = token;
        AssignTo(userId, platform, now);
    }

    public string Token { get; private set; }

    public Guid UserId { get; private set; }

    public PushPlatform Platform { get; private set; }

    /// <summary>The last registration. The app reports its token at every start, so this approximates "last used".</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The same phone, possibly another account — the previous owner must not keep getting their pushes on it.</summary>
    public void AssignTo(Guid userId, PushPlatform platform, DateTimeOffset now)
    {
        UserId = userId;
        Platform = platform;
        UpdatedAt = now;
    }
}
