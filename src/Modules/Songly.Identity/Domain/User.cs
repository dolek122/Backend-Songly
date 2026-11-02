namespace Songly.Identity.Domain;

/// <summary>
/// An account: login data and global role. Deliberately separate from the public profile
/// (original Claude.md §5): credentials are sensitive, profile data is public.
/// </summary>
/// <remarks>
/// Maps only the columns this part of the module uses; the suspension appeal columns join when
/// suspensions are ported.
/// </remarks>
internal sealed class User
{
    // For EF Core.
    private User()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    public User(Guid id, string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Always normalized (trimmed, lower case) before it gets here.</summary>
    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Global administrator role (original ADR 0006, 0021). No setter on purpose: granting and
    /// revoking are named operations, the one place privilege escalation goes through.
    /// </summary>
    public bool IsAdmin { get; private set; }

    /// <summary><c>null</c> = the account is active (original ADR 0022).</summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary><c>null</c> while suspended = indefinitely.</summary>
    public DateTimeOffset? SuspensionExpiresAt { get; private set; }

    public void ChangePassword(string newPasswordHash) => PasswordHash = newPasswordHash;

    /// <summary>
    /// Expiry is lazy: the row stays, a suspension past its end simply stops counting. No
    /// background job wakes up when it ends.
    /// </summary>
    public bool IsSuspendedAt(DateTimeOffset now) => IsSuspended(SuspendedAt, SuspensionExpiresAt, now);

    /// <summary>The same rule for queries that read the two columns without loading the entity.</summary>
    public static bool IsSuspended(DateTimeOffset? suspendedAt, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        suspendedAt is not null && (expiresAt is null || expiresAt > now);
}
