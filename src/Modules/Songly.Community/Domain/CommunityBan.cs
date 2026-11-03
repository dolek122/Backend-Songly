namespace Songly.Community.Domain;

/// <summary>
/// A ban from a community. It cuts off <b>writing</b>, not reading: communities are public, so
/// hiding their content from a banned user would be a pretence — logging out is enough.
/// </summary>
/// <remarks>
/// A separate table, not a flag on the membership: the row has to outlive the membership, or
/// "leave and join again" would undo the ban in one request.
/// </remarks>
internal sealed class CommunityBan
{
    // For EF Core.
    private CommunityBan()
    {
    }

    public CommunityBan(Guid communityId, Guid userId, Guid bannedBy, string? reason, DateTimeOffset now, DateTimeOffset? expiresAt)
    {
        CommunityId = communityId;
        UserId = userId;
        BannedBy = bannedBy;
        Reason = reason;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid CommunityId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>
    /// <c>null</c> once the moderator who imposed the ban deleted their account. The ban belongs to
    /// the community, not to that person: their leaving must not quietly let back in everyone they
    /// banned.
    /// </summary>
    public Guid? BannedBy { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary><c>null</c> = permanent (original ADR 0014).</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public string? AppealText { get; private set; }

    public DateTimeOffset? AppealedAt { get; private set; }

    public BanAppealStatus? AppealStatus { get; private set; }

    public DateTimeOffset? AppealEscalatedAt { get; private set; }

    /// <summary>Expiry is lazy: nothing happens when a ban runs out — whoever asks, asks about an <i>active</i> ban.</summary>
    public bool IsActiveAt(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;

    /// <summary>
    /// A new ban after the previous one expired. It overwrites the row whole — the primary key allows
    /// one — and clears the appeal: a new ban is a new case, with a fresh right to appeal even after
    /// a rejected one.
    /// </summary>
    public void Reimpose(Guid bannedBy, string? reason, DateTimeOffset now, DateTimeOffset? expiresAt)
    {
        BannedBy = bannedBy;
        Reason = reason;
        CreatedAt = now;
        ExpiresAt = expiresAt;
        AppealText = null;
        AppealedAt = null;
        AppealStatus = null;
        AppealEscalatedAt = null;
    }

    /// <summary>One appeal per ban, whatever became of it: the structural anti-spam of a channel that runs from the punished to the punisher.</summary>
    public void SubmitAppeal(string text, DateTimeOffset now)
    {
        AppealText = text;
        AppealedAt = now;
        AppealStatus = BanAppealStatus.Pending;
    }

    public void RejectAppeal() => AppealStatus = BanAppealStatus.Rejected;

    public void EscalateAppeal(DateTimeOffset now)
    {
        AppealStatus = BanAppealStatus.Escalated;
        AppealEscalatedAt = now;
    }

    public void RejectEscalation() => AppealStatus = BanAppealStatus.EscalationRejected;
}
