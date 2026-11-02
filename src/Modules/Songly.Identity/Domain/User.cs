namespace Songly.Identity.Domain;

/// <summary>
/// An account: login data, global role and the platform-wide sanction (suspension). Deliberately
/// separate from the public profile (original Claude.md §5): credentials are sensitive, profile
/// data is public.
/// </summary>
internal sealed class User
{
    // For EF Core.
    private User()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    public User(string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
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
    /// revoking are named operations — the one place privilege escalation goes through, and it
    /// should be found by searching for its name.
    /// </summary>
    public bool IsAdmin { get; private set; }

    /// <summary><c>null</c> = the account is active (original ADR 0022).</summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary>Mandatory while suspended; shown to the suspended user, through the pull channel only.</summary>
    public string? SuspensionReason { get; private set; }

    /// <summary><c>null</c> while suspended = indefinitely.</summary>
    public DateTimeOffset? SuspensionExpiresAt { get; private set; }

    /// <summary>The appeal against the current suspension (original ADR 0023); <c>null</c> = none.</summary>
    public string? SuspensionAppealText { get; private set; }

    public DateTimeOffset? SuspensionAppealedAt { get; private set; }

    public SuspensionAppealStatus? SuspensionAppealStatus { get; private set; }

    public bool HasAppeal => SuspensionAppealStatus is not null;

    public bool HasPendingAppeal => SuspensionAppealStatus == Domain.SuspensionAppealStatus.Pending;

    public void ChangePassword(string newPasswordHash) => PasswordHash = newPasswordHash;

    public void GrantAdmin() => IsAdmin = true;

    public void RevokeAdmin() => IsAdmin = false;

    /// <summary>
    /// Expiry is lazy: the row stays, a suspension past its end simply stops counting. No
    /// background job wakes up when it ends.
    /// </summary>
    public bool IsSuspendedAt(DateTimeOffset now) => IsSuspended(SuspendedAt, SuspensionExpiresAt, now);

    /// <summary>The same rule for queries that read the two columns without loading the entity.</summary>
    public static bool IsSuspended(DateTimeOffset? suspendedAt, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        suspendedAt is not null && (expiresAt is null || expiresAt > now);

    /// <summary>
    /// Suspends the account, or re-decides an ongoing suspension (new reason, new term) — unlike a
    /// community ban, whose active row is untouchable. Only an administrator suspends, so there is
    /// no race of two moderators that rule protected against.
    /// </summary>
    /// <remarks>
    /// The appeal is reset only when an <b>active</b> account gets suspended: a new case brings a
    /// fresh right to appeal, even after an earlier rejection. Correcting the reason or the term of
    /// an ongoing suspension is the same case — clearing the appeal then would silently delete a
    /// petition already filed.
    /// </remarks>
    /// <returns><c>true</c> when the account went from active to suspended.</returns>
    public bool Suspend(string reason, DateTimeOffset now, DateTimeOffset? expiresAt)
    {
        var wasActive = !IsSuspendedAt(now);
        SuspendedAt = now;
        SuspensionReason = reason;
        SuspensionExpiresAt = expiresAt;
        if (wasActive)
        {
            ResetAppeal();
        }

        return wasActive;
    }

    public void LiftSuspension()
    {
        SuspendedAt = null;
        SuspensionReason = null;
        SuspensionExpiresAt = null;
        ResetAppeal();
    }

    /// <summary>One appeal per suspension, whatever happened to it — the caller checks <see cref="HasAppeal"/>.</summary>
    public void SubmitAppeal(string text, DateTimeOffset now)
    {
        SuspensionAppealText = text;
        SuspensionAppealedAt = now;
        SuspensionAppealStatus = Domain.SuspensionAppealStatus.Pending;
    }

    public void RejectAppeal() => SuspensionAppealStatus = Domain.SuspensionAppealStatus.Rejected;

    private void ResetAppeal()
    {
        SuspensionAppealText = null;
        SuspensionAppealedAt = null;
        SuspensionAppealStatus = null;
    }
}
