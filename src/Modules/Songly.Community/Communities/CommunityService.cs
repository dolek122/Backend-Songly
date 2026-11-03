using Microsoft.EntityFrameworkCore;
using Songly.Common.Paging;
using Songly.Community.Contracts;
using Songly.Community.Domain;
using Songly.Community.Http;
using Songly.Community.Persistence;
using Songly.Profile.Contracts;

namespace Songly.Community.Communities;

/// <summary>Communities, membership and roles.</summary>
/// <remarks>
/// The original publishes <c>CommunityMemberJoinedEvent</c>, <c>CommunityMemberRoleChangedEvent</c>
/// and <c>CommunityDeletedEvent</c>; they arrive with their consumers (notification, content) —
/// publishing to nobody would be code without behaviour.
/// </remarks>
internal sealed class CommunityService(CommunityDbContext db, IProfileDirectory profiles, TimeProvider time)
    : ICommunityMembership, ICommunityDirectory
{
    /// <summary>The creator becomes the owner — and a member, so that lists and counts include them.</summary>
    public async Task<CommunityResponse> CreateAsync(Guid ownerId, CreateCommunityRequest request, CancellationToken cancellationToken)
    {
        await RequireNameFreeAsync(request.Name!, cancellationToken);
        var now = time.GetUtcNow();
        var community = new CommunityEntity(request.Name!, request.Description, ownerId, now);
        db.Communities.Add(community);

        // Saved first: the membership row references the community, and EF Core does not know that
        // (no navigation is mapped), so it would not order the inserts.
        await db.SaveChangesAsync(cancellationToken);
        db.Members.Add(new CommunityMember(community.Id, ownerId, CommunityRole.Owner, now));
        await db.SaveChangesAsync(cancellationToken);
        return await ToResponseAsync(community, ownerId, cancellationToken);
    }

    public async Task<CommunityResponse> UpdateAsync(Guid communityId, Guid requesterId, UpdateCommunityRequest request, CancellationToken cancellationToken)
    {
        var community = await RequireOwnedAsync(communityId, requesterId, cancellationToken);

        // Keeping one's own name — in another letter case, too — is not a collision.
        if (!string.Equals(community.Name, request.Name, StringComparison.OrdinalIgnoreCase))
        {
            await RequireNameFreeAsync(request.Name!, cancellationToken);
        }

        community.Update(request.Name!, request.Description);
        await db.SaveChangesAsync(cancellationToken);
        return await ToResponseAsync(community, requesterId, cancellationToken);
    }

    /// <summary>The memberships go with it (<c>ON DELETE CASCADE</c> inside this module's own tables).</summary>
    public async Task DeleteAsync(Guid communityId, Guid requesterId, CancellationToken cancellationToken)
    {
        await RequireOwnedAsync(communityId, requesterId, cancellationToken);
        await db.Communities.Where(c => c.Id == communityId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<CommunityResponse> GetAsync(Guid communityId, Guid viewerId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await RequireAsync(communityId, cancellationToken), viewerId, cancellationToken);

    public async Task<PagedResponse<CommunityResponse>> ListAsync(Guid viewerId, PageQuery page, CancellationToken cancellationToken)
    {
        var communities = await db.Communities.AsNoTracking()
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .ToPageAsync(page, community => community, cancellationToken);
        var ids = communities.Content.Select(c => c.Id).ToList();
        var myRoles = await db.Members
            .Where(m => m.UserId == viewerId && ids.Contains(m.CommunityId))
            .ToDictionaryAsync(m => m.CommunityId, m => m.Role, cancellationToken);
        return await ToPageAsync(communities, community => myRoles.TryGetValue(community.Id, out var role) ? role : null, cancellationToken);
    }

    public async Task<PagedResponse<CommunityResponse>> ListMineAsync(Guid userId, PageQuery page, CancellationToken cancellationToken)
    {
        var memberships = await db.Members.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.JoinedAt).ThenBy(m => m.CommunityId)
            .ToPageAsync(page, membership => membership, cancellationToken);
        var ids = memberships.Content.Select(m => m.CommunityId).ToList();
        var communities = await db.Communities.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var roles = memberships.Content.ToDictionary(m => m.CommunityId, m => m.Role);

        var ordered = new PagedResponse<CommunityEntity>(ids.Select(id => communities[id]).ToList(), memberships.Page);
        return await ToPageAsync(ordered, community => roles[community.Id], cancellationToken);
    }

    /// <summary>Idempotent: joining again leaves the membership as it is.</summary>
    public async Task JoinAsync(Guid communityId, Guid userId, CancellationToken cancellationToken)
    {
        await RequireAsync(communityId, cancellationToken);
        if (!await IsMemberAsync(communityId, userId, cancellationToken))
        {
            db.Members.Add(new CommunityMember(communityId, userId, CommunityRole.Member, time.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Idempotent for someone who is not a member. The owner cannot leave: a community without an
    /// owner would have nobody to hand out roles or delete it.
    /// </summary>
    public async Task LeaveAsync(Guid communityId, Guid userId, CancellationToken cancellationToken)
    {
        await RequireAsync(communityId, cancellationToken);
        var membership = await db.Members.SingleOrDefaultAsync(m => m.CommunityId == communityId && m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return;
        }

        if (membership.Role == CommunityRole.Owner)
        {
            throw new OwnerCannotLeaveException();
        }

        db.Members.Remove(membership);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<CommunityMemberResponse>> ListMembersAsync(Guid communityId, PageQuery page, CancellationToken cancellationToken)
    {
        await RequireAsync(communityId, cancellationToken);
        var members = await db.Members.AsNoTracking()
            .Where(m => m.CommunityId == communityId)
            .OrderBy(m => m.JoinedAt).ThenBy(m => m.UserId)
            .ToPageAsync(page, member => member, cancellationToken);
        var ids = members.Content.Select(m => m.UserId).ToList();
        var names = await profiles.DisplayNamesAsync(ids, cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync(ids, cancellationToken);
        return new PagedResponse<CommunityMemberResponse>(
            members.Content.Select(m => new CommunityMemberResponse(m.UserId, names.GetValueOrDefault(m.UserId), avatars.GetValueOrDefault(m.UserId), ToDto(m.Role), m.JoinedAt)).ToList(),
            members.Page);
    }

    /// <summary>
    /// Gives a member a role. <b>The owner only</b> — a moderator able to hand out roles could demote
    /// the owner and take the community over. <c>OWNER</c> cannot be given here, and the owner cannot
    /// be demoted, not even by their own hand: that would leave the community without an owner.
    /// Idempotent: the same role again changes nothing.
    /// </summary>
    public async Task SetMemberRoleAsync(Guid communityId, Guid requesterId, Guid targetUserId, CommunityRoleDto role, CancellationToken cancellationToken)
    {
        await RequireOwnedAsync(communityId, requesterId, cancellationToken);
        if (role == CommunityRoleDto.Owner)
        {
            throw new CannotAssignOwnerRoleException();
        }

        var membership = await db.Members.SingleOrDefaultAsync(m => m.CommunityId == communityId && m.UserId == targetUserId, cancellationToken)
            ?? throw new NotCommunityMemberToModerateException();
        if (membership.Role == CommunityRole.Owner)
        {
            throw new CannotModerateOwnerException();
        }

        membership.ChangeRole(role == CommunityRoleDto.Moderator ? CommunityRole.Moderator : CommunityRole.Member);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The caller's own standing (original ADR 0015).</summary>
    public async Task<MembershipStatusResponse> MembershipStatusAsync(Guid communityId, Guid userId, CancellationToken cancellationToken)
    {
        await RequireAsync(communityId, cancellationToken);
        var membership = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.CommunityId == communityId && m.UserId == userId, cancellationToken);
        return membership is null
            ? new MembershipStatusResponse(CommunityMembershipStatus.None, null, null, null)
            : new MembershipStatusResponse(CommunityMembershipStatus.Member, ToDto(membership.Role), membership.JoinedAt, null);
    }

    public Task<bool> ExistsAsync(Guid communityId, CancellationToken cancellationToken) =>
        db.Communities.AnyAsync(c => c.Id == communityId, cancellationToken);

    public Task<bool> IsMemberAsync(Guid communityId, Guid userId, CancellationToken cancellationToken) =>
        db.Members.AnyAsync(m => m.CommunityId == communityId && m.UserId == userId, cancellationToken);

    public Task<bool> IsOwnerAsync(Guid communityId, Guid userId, CancellationToken cancellationToken) =>
        db.Communities.AnyAsync(c => c.Id == communityId && c.OwnerId == userId, cancellationToken);

    public Task<bool> CanModerateAsync(Guid communityId, Guid userId, CancellationToken cancellationToken) =>
        db.Members.AnyAsync(m => m.CommunityId == communityId && m.UserId == userId && CommunityRoles.Moderating.Contains(m.Role), cancellationToken);

    public async Task<IReadOnlySet<Guid>> ModeratorIdsAsync(Guid communityId, CancellationToken cancellationToken) =>
        (await db.Members
            .Where(m => m.CommunityId == communityId && CommunityRoles.Moderating.Contains(m.Role))
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public Task<string?> NameOfAsync(Guid communityId, CancellationToken cancellationToken) =>
        db.Communities.Where(c => c.Id == communityId).Select(c => (string?)c.Name).SingleOrDefaultAsync(cancellationToken);

    private async Task RequireNameFreeAsync(string name, CancellationToken cancellationToken)
    {
        var lower = name.ToLowerInvariant();
        if (await db.Communities.AnyAsync(c => c.Name.ToLower() == lower, cancellationToken))
        {
            throw new CommunityNameTakenException();
        }
    }

    private async Task<CommunityEntity> RequireAsync(Guid communityId, CancellationToken cancellationToken) =>
        await db.Communities.SingleOrDefaultAsync(c => c.Id == communityId, cancellationToken) ?? throw new CommunityNotFoundException();

    private async Task<CommunityEntity> RequireOwnedAsync(Guid communityId, Guid requesterId, CancellationToken cancellationToken)
    {
        var community = await RequireAsync(communityId, cancellationToken);
        return community.OwnerId == requesterId ? community : throw new NotCommunityOwnerException();
    }

    private async Task<CommunityResponse> ToResponseAsync(CommunityEntity community, Guid viewerId, CancellationToken cancellationToken)
    {
        var role = await db.Members.Where(m => m.CommunityId == community.Id && m.UserId == viewerId).Select(m => (CommunityRole?)m.Role).SingleOrDefaultAsync(cancellationToken);
        var page = new PagedResponse<CommunityEntity>([community], new PageMetadata(1, 0, 1, 1));
        return (await ToPageAsync(page, _ => role, cancellationToken)).Content[0];
    }

    /// <summary>Member counts and owners' names for a whole page in one query each — no N+1 to profile.</summary>
    private async Task<PagedResponse<CommunityResponse>> ToPageAsync(PagedResponse<CommunityEntity> page, Func<CommunityEntity, CommunityRole?> myRole, CancellationToken cancellationToken)
    {
        var ids = page.Content.Select(c => c.Id).ToList();
        var counts = await db.Members
            .Where(m => ids.Contains(m.CommunityId))
            .GroupBy(m => m.CommunityId)
            .Select(group => new { group.Key, Count = group.LongCount() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        var ownerIds = page.Content.Select(c => c.OwnerId).Distinct().ToList();
        var names = await profiles.DisplayNamesAsync(ownerIds, cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync(ownerIds, cancellationToken);

        return new PagedResponse<CommunityResponse>(
            page.Content.Select(c => new CommunityResponse(
                c.Id,
                c.Name,
                c.Description,
                c.OwnerId,
                names.GetValueOrDefault(c.OwnerId),
                avatars.GetValueOrDefault(c.OwnerId),
                counts.GetValueOrDefault(c.Id),
                myRole(c) is { } role ? ToDto(role) : null,
                c.CreatedAt)).ToList(),
            page.Page);
    }

    private static CommunityRoleDto ToDto(CommunityRole role) => role switch
    {
        CommunityRole.Owner => CommunityRoleDto.Owner,
        CommunityRole.Moderator => CommunityRoleDto.Moderator,
        CommunityRole.Member => CommunityRoleDto.Member,
        _ => throw new InvalidOperationException($"Unknown community role {role}"),
    };
}
