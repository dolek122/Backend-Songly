using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Common.Paging;
using Songly.Community.Contracts;
using Songly.Community.Domain;
using Songly.Community.Http;
using Songly.Community.Persistence;
using Songly.Profile.Contracts;

namespace Songly.Community.Communities;

/// <summary>
/// Bans and the appeals against them: the community's moderators first (original ADR 0011, 0017),
/// the platform's administration second, on the banned user's request only (ADR 0028).
/// </summary>
/// <remarks>
/// Each decision the inbox tells someone about is published after its save: a new ban, an appeal, its
/// acceptance and rejection, the escalation, the second instance's decision, and every lifting of an
/// active ban (<see cref="CommunityBanLifted"/>), for the moderation log.
/// </remarks>
internal sealed class BanService(CommunityDbContext db, CommunityService communities, IProfileDirectory profiles, IDomainEvents events, TimeProvider time)
{
    /// <summary>
    /// Bans a user: removes the membership and blocks the return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owner cannot be banned, and a moderator cannot ban a moderator — otherwise two moderators
    /// could throw each other out, and whoever clicked first would win. The owner bans anyone.
    /// </para>
    /// <para>
    /// Idempotent for an <b>active</b> ban: banning again is no change, and does not change the term
    /// either — making a ban longer or shorter is unban + ban, two deliberate decisions rather than a
    /// quiet swap on a duplicated click. An <b>expired</b> ban is different (original ADR 0014): its
    /// row stays only because the key allows one, and a new ban overwrites it as a new decision.
    /// </para>
    /// </remarks>
    public async Task BanAsync(Guid communityId, Guid requesterId, Guid targetUserId, BanMemberRequest? request, CancellationToken cancellationToken)
    {
        var community = await communities.RequireAsync(communityId, cancellationToken);
        await RequireModeratorAsync(communityId, requesterId, cancellationToken);
        if (community.OwnerId == targetUserId)
        {
            throw new CannotModerateOwnerException();
        }

        if (requesterId != community.OwnerId && await communities.CanModerateAsync(communityId, targetUserId, cancellationToken))
        {
            throw new CannotModerateModeratorException();
        }

        var now = time.GetUtcNow();
        var expiresAt = request?.DurationDays is { } days ? now.AddDays(days) : (DateTimeOffset?)null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Members.Where(m => m.CommunityId == communityId && m.UserId == targetUserId).ExecuteDeleteAsync(cancellationToken);
        var existing = await db.Bans.SingleOrDefaultAsync(b => b.CommunityId == communityId && b.UserId == targetUserId, cancellationToken);
        var banned = existing is null || !existing.IsActiveAt(now);
        if (existing is null)
        {
            db.Bans.Add(new CommunityBan(communityId, targetUserId, requesterId, request?.Reason, now, expiresAt));
        }
        else if (banned)
        {
            existing.Reimpose(requesterId, request?.Reason, now, expiresAt);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Only a ban that changed the state — a repeated click on an active ban tells nobody anything new.
        if (banned)
        {
            await events.PublishAsync(new CommunityMemberBanned(communityId, targetUserId, requesterId, expiresAt, now), cancellationToken);
        }
    }

    /// <summary>
    /// Idempotent. Lifting an active ban with a pending appeal <b>is</b> accepting that appeal (original
    /// ADR 0017) — no separate endpoint, because it is the same decision; without this rule a moderator
    /// lifting the ban directly would leave the petition unanswered while answering it. An expired ban is
    /// moot: clearing a dead row accepts nothing.
    /// </summary>
    public async Task UnbanAsync(Guid communityId, Guid requesterId, Guid targetUserId, CancellationToken cancellationToken)
    {
        await communities.RequireAsync(communityId, cancellationToken);
        await RequireModeratorAsync(communityId, requesterId, cancellationToken);
        var now = time.GetUtcNow();
        var active = await ActiveBanAsync(communityId, targetUserId, cancellationToken);
        var deleted = await db.Bans.Where(b => b.CommunityId == communityId && b.UserId == targetUserId).ExecuteDeleteAsync(cancellationToken);
        if (active is null || deleted == 0)
        {
            return;
        }

        if (active.AppealStatus == BanAppealStatus.Pending)
        {
            await events.PublishAsync(new CommunityBanAppealAccepted(communityId, targetUserId, requesterId, now), cancellationToken);
        }

        await events.PublishAsync(new CommunityBanLifted(communityId, targetUserId, requesterId, now), cancellationToken);
    }

    /// <summary>
    /// The moderators' list — active bans only, newest first. The only list in the module that is not
    /// public: who is banned and why is information about a person, not about the community.
    /// </summary>
    public async Task<PagedResponse<CommunityBanResponse>> ListAsync(Guid communityId, Guid requesterId, PageQuery page, CancellationToken cancellationToken)
    {
        await communities.RequireAsync(communityId, cancellationToken);
        await RequireModeratorAsync(communityId, requesterId, cancellationToken);
        var now = time.GetUtcNow();
        var bans = await db.Bans.AsNoTracking()
            .Where(b => b.CommunityId == communityId && (b.ExpiresAt == null || b.ExpiresAt > now))
            .OrderByDescending(b => b.CreatedAt).ThenBy(b => b.UserId)
            .ToPageAsync(page, ban => ban, cancellationToken);
        var ids = bans.Content.Select(b => b.UserId).ToList();
        var names = await profiles.DisplayNamesAsync(ids, cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync(ids, cancellationToken);
        return new PagedResponse<CommunityBanResponse>(
            bans.Content.Select(b => new CommunityBanResponse(
                b.UserId,
                names.GetValueOrDefault(b.UserId),
                avatars.GetValueOrDefault(b.UserId),
                b.Reason,
                b.BannedBy,
                b.CreatedAt,
                b.ExpiresAt,
                b.AppealText,
                b.AppealedAt,
                BanMapping.ToDto(b.AppealStatus))).ToList(),
            bans.Page);
    }

    /// <summary>
    /// An appeal against one's own <b>active</b> ban — an expired one is no ban, there is nothing to
    /// appeal. One per ban, whatever became of it.
    /// </summary>
    public async Task AppealAsync(Guid communityId, Guid userId, string text, CancellationToken cancellationToken)
    {
        await communities.RequireAsync(communityId, cancellationToken);
        var ban = await ActiveBanAsync(communityId, userId, cancellationToken) ?? throw new NoBanToAppealException();
        if (ban.AppealStatus is not null)
        {
            throw new BanAppealAlreadySubmittedException();
        }

        var now = time.GetUtcNow();
        ban.SubmitAppeal(text, now);
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new CommunityBanAppealSubmitted(communityId, userId, now), cancellationToken);
    }

    /// <summary>Rejecting the appeal: the ban stays, and this ban's case is closed — at the first instance.</summary>
    public async Task RejectAppealAsync(Guid communityId, Guid requesterId, Guid targetUserId, CancellationToken cancellationToken)
    {
        await communities.RequireAsync(communityId, cancellationToken);
        await RequireModeratorAsync(communityId, requesterId, cancellationToken);
        var ban = await ActiveBanAsync(communityId, targetUserId, cancellationToken);
        if (ban?.AppealStatus != BanAppealStatus.Pending)
        {
            throw new BanAppealNotFoundException();
        }

        ban.RejectAppeal();
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new CommunityBanAppealRejected(communityId, targetUserId, requesterId, time.GetUtcNow()), cancellationToken);
    }

    /// <summary>
    /// Sends a rejected appeal to the <b>second instance</b> — the platform's administration
    /// (original ADR 0028). From "rejected" only: escalating before the first instance ruled would
    /// bypass the community's moderators, who are the default instance. Once: from "rejected" there is
    /// no way back, so there is nothing to escalate a second time.
    /// </summary>
    public async Task EscalateAppealAsync(Guid communityId, Guid userId, CancellationToken cancellationToken)
    {
        await communities.RequireAsync(communityId, cancellationToken);
        var ban = await ActiveBanAsync(communityId, userId, cancellationToken) ?? throw new NoBanToAppealException();
        if (ban.AppealStatus != BanAppealStatus.Rejected)
        {
            throw new BanAppealNotEscalatableException();
        }

        var now = time.GetUtcNow();
        ban.EscalateAppeal(now);
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new CommunityBanAppealEscalated(communityId, userId, now), cancellationToken);
    }

    /// <summary>
    /// The second instance's queue, oldest first — a queue, not a stack. Not paged, like the other
    /// admin lists: there are only as many escalations as cases that went through two levels.
    /// </summary>
    public async Task<IReadOnlyList<EscalatedAppealResponse>> EscalatedAppealsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var escalated = await db.Bans.AsNoTracking()
            .Where(b => b.AppealStatus == BanAppealStatus.Escalated && (b.ExpiresAt == null || b.ExpiresAt > now))
            .OrderBy(b => b.AppealEscalatedAt).ThenBy(b => b.CommunityId).ThenBy(b => b.UserId)
            .ToListAsync(cancellationToken);
        var communityIds = escalated.Select(b => b.CommunityId).Distinct().ToList();
        var names = await db.Communities.Where(c => communityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        return escalated
            .Select(b => new EscalatedAppealResponse(b.CommunityId, names.GetValueOrDefault(b.CommunityId), b.UserId, b.Reason, b.CreatedAt, b.ExpiresAt, b.AppealText, b.AppealedAt, b.AppealEscalatedAt))
            .ToList();
    }

    /// <summary>
    /// The second instance <b>overturns</b> the ban. The only operation in this module done by someone
    /// outside the community — so it requires an escalation first: the administration does not review
    /// bans on its own initiative, it decides cases the banned user sent to it. The community's
    /// autonomy stays the rule; the platform's review is the exception. It does not forbid the
    /// community to ban again — that would be a real limit on its autonomy.
    /// </summary>
    public async Task OverturnAsync(Guid communityId, Guid targetUserId, Guid adminId, CancellationToken cancellationToken)
    {
        await communities.RequireAsync(communityId, cancellationToken);
        var ban = await EscalatedBanAsync(communityId, targetUserId, cancellationToken);
        db.Bans.Remove(ban);
        await db.SaveChangesAsync(cancellationToken);
        var now = time.GetUtcNow();
        await events.PublishAsync(new CommunityBanEscalationAccepted(communityId, targetUserId, adminId, now), cancellationToken);
        await events.PublishAsync(new CommunityBanLifted(communityId, targetUserId, adminId, now), cancellationToken);
    }

    /// <summary>The second instance keeps the ban — the end of the ladder.</summary>
    public async Task RejectEscalationAsync(Guid communityId, Guid targetUserId, Guid adminId, CancellationToken cancellationToken)
    {
        await communities.RequireAsync(communityId, cancellationToken);
        var ban = await EscalatedBanAsync(communityId, targetUserId, cancellationToken);
        ban.RejectEscalation();
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new CommunityBanEscalationRejected(communityId, targetUserId, adminId, time.GetUtcNow()), cancellationToken);
    }

    private async Task<CommunityBan> EscalatedBanAsync(Guid communityId, Guid userId, CancellationToken cancellationToken)
    {
        var ban = await ActiveBanAsync(communityId, userId, cancellationToken);
        return ban?.AppealStatus == BanAppealStatus.Escalated ? ban : throw new EscalatedAppealNotFoundException();
    }

    private async Task<CommunityBan?> ActiveBanAsync(Guid communityId, Guid userId, CancellationToken cancellationToken)
    {
        var ban = await db.Bans.SingleOrDefaultAsync(b => b.CommunityId == communityId && b.UserId == userId, cancellationToken);
        return ban is not null && ban.IsActiveAt(time.GetUtcNow()) ? ban : null;
    }

    private async Task RequireModeratorAsync(Guid communityId, Guid requesterId, CancellationToken cancellationToken)
    {
        if (!await communities.CanModerateAsync(communityId, requesterId, cancellationToken))
        {
            throw new NotCommunityModeratorException();
        }
    }
}
