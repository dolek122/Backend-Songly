using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Common.Paging;
using Songly.Profile.Contracts;
using Songly.Social.Contracts;
using Songly.Social.Domain;
using Songly.Social.Http;
using Songly.Social.Persistence;

namespace Songly.Social.Graph;

/// <summary>Friend requests, friendships and blocks.</summary>
/// <remarks>
/// A sent request is published as <see cref="FriendRequestSent"/>, an accepted one — also one accepted
/// by a request the other way — as <see cref="FriendRequestAccepted"/>, each after the save.
/// </remarks>
internal sealed class SocialService(SocialDbContext db, IProfileDirectory profiles, IDomainEvents events, TimeProvider time) : ISocialGraph
{
    /// <summary>
    /// Sends a request. When the addressee had already sent one the other way, that one is accepted
    /// instead of creating a duplicate: both people asked for the friendship.
    /// </summary>
    public async Task<FriendRequestResponse> SendRequestAsync(Guid requesterId, Guid addresseeId, CancellationToken cancellationToken)
    {
        if (requesterId == addresseeId)
        {
            throw new SelfActionNotAllowedException();
        }

        await RequireUserExistsAsync(addresseeId, cancellationToken);
        if (await IsBlockedEitherWayAsync(requesterId, addresseeId, cancellationToken))
        {
            throw new UserBlockedException();
        }

        var (a, b) = Friendship.CanonicalPair(requesterId, addresseeId);
        if (await db.Friendships.AnyAsync(f => f.UserAId == a && f.UserBId == b, cancellationToken))
        {
            throw new AlreadyFriendsException();
        }

        if (await PendingAsync(requesterId, addresseeId, cancellationToken) is not null)
        {
            throw new FriendRequestAlreadyPendingException();
        }

        var now = time.GetUtcNow();
        if (await PendingAsync(addresseeId, requesterId, cancellationToken) is { } reverse)
        {
            reverse.Accept(now);
            db.Friendships.Add(Friendship.Between(requesterId, addresseeId, now));
            await db.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(new FriendRequestAccepted(reverse.Id, reverse.RequesterId, reverse.AddresseeId, now), cancellationToken);
            return await ToResponseAsync(reverse, cancellationToken);
        }

        var request = new FriendRequest(requesterId, addresseeId, now);
        db.FriendRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new FriendRequestSent(request.Id, requesterId, addresseeId, now), cancellationToken);
        return await ToResponseAsync(request, cancellationToken);
    }

    public async Task<PagedResponse<FriendRequestResponse>> ListRequestsAsync(Guid userId, FriendRequestDirection direction, PageQuery page, CancellationToken cancellationToken)
    {
        var pending = db.FriendRequests.AsNoTracking().Where(r => r.Status == FriendRequestStatus.Pending);
        pending = direction == FriendRequestDirection.Incoming ? pending.Where(r => r.AddresseeId == userId) : pending.Where(r => r.RequesterId == userId);
        var requests = await pending.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToPageAsync(page, request => request, cancellationToken);

        var people = await PeopleAsync(requests.Content.SelectMany(r => new[] { r.RequesterId, r.AddresseeId }), cancellationToken);
        return new PagedResponse<FriendRequestResponse>(requests.Content.Select(r => ToResponse(r, people)).ToList(), requests.Page);
    }

    public async Task<FriendRequestResponse> AcceptRequestAsync(Guid requestId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var request = await PendingRequestForAsync(requestId, r => r.AddresseeId == currentUserId, cancellationToken);
        var now = time.GetUtcNow();
        request.Accept(now);
        db.Friendships.Add(Friendship.Between(request.RequesterId, request.AddresseeId, now));
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new FriendRequestAccepted(request.Id, request.RequesterId, request.AddresseeId, now), cancellationToken);
        return await ToResponseAsync(request, cancellationToken);
    }

    public async Task<FriendRequestResponse> DeclineRequestAsync(Guid requestId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var request = await PendingRequestForAsync(requestId, r => r.AddresseeId == currentUserId, cancellationToken);
        request.Decline(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await ToResponseAsync(request, cancellationToken);
    }

    /// <summary>Deleted, not marked: the same request can be sent again afterwards.</summary>
    public async Task CancelRequestAsync(Guid requestId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var request = await PendingRequestForAsync(requestId, r => r.RequesterId == currentUserId, cancellationToken);
        db.FriendRequests.Remove(request);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<FriendResponse>> ListFriendsAsync(Guid userId, PageQuery page, CancellationToken cancellationToken)
    {
        var friendships = await db.Friendships.AsNoTracking()
            .Where(f => f.UserAId == userId || f.UserBId == userId)
            .OrderByDescending(f => f.CreatedAt).ThenBy(f => f.UserAId).ThenBy(f => f.UserBId)
            .ToPageAsync(page, friendship => friendship, cancellationToken);

        var people = await PeopleAsync(friendships.Content.Select(f => f.OtherThan(userId)), cancellationToken);
        return new PagedResponse<FriendResponse>(
            friendships.Content.Select(f =>
            {
                var friend = f.OtherThan(userId);
                return new FriendResponse(friend, people.Names.GetValueOrDefault(friend), people.Avatars.GetValueOrDefault(friend), f.CreatedAt);
            }).ToList(),
            friendships.Page);
    }

    public async Task RemoveFriendAsync(Guid userId, Guid friendId, CancellationToken cancellationToken)
    {
        var (a, b) = Friendship.CanonicalPair(userId, friendId);
        if (await db.Friendships.Where(f => f.UserAId == a && f.UserBId == b).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw new FriendshipNotFoundException();
        }
    }

    /// <summary>
    /// Idempotent. A block ends the friendship and removes pending requests in both directions:
    /// after unblocking, the relation has to be built again from scratch.
    /// </summary>
    public async Task BlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken)
    {
        if (blockerId == blockedId)
        {
            throw new SelfActionNotAllowedException();
        }

        await RequireUserExistsAsync(blockedId, cancellationToken);
        var (a, b) = Friendship.CanonicalPair(blockerId, blockedId);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Blocks.AnyAsync(x => x.BlockerId == blockerId && x.BlockedId == blockedId, cancellationToken))
        {
            db.Blocks.Add(new Block(blockerId, blockedId, time.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }

        await db.Friendships.Where(f => f.UserAId == a && f.UserBId == b).ExecuteDeleteAsync(cancellationToken);
        await db.FriendRequests
            .Where(r => r.Status == FriendRequestStatus.Pending
                && ((r.RequesterId == blockerId && r.AddresseeId == blockedId) || (r.RequesterId == blockedId && r.AddresseeId == blockerId)))
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        db.Blocks.Where(x => x.BlockerId == blockerId && x.BlockedId == blockedId).ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Whether the caller blocks the user. No check that the user exists: for a nonexistent one "you
    /// do not block them" is true, and tells nothing the profile would not.
    /// </summary>
    public Task<bool> IsBlockingAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        db.Blocks.AnyAsync(x => x.BlockerId == blockerId && x.BlockedId == blockedId, cancellationToken);

    public async Task<PagedResponse<BlockedUserResponse>> ListBlockedAsync(Guid blockerId, PageQuery page, CancellationToken cancellationToken)
    {
        var blocks = await db.Blocks.AsNoTracking()
            .Where(x => x.BlockerId == blockerId)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.BlockedId)
            .ToPageAsync(page, block => block, cancellationToken);

        var names = await profiles.DisplayNamesAsync(blocks.Content.Select(x => x.BlockedId).ToList(), cancellationToken);
        return new PagedResponse<BlockedUserResponse>(
            blocks.Content.Select(x => new BlockedUserResponse(x.BlockedId, names.GetValueOrDefault(x.BlockedId), x.CreatedAt)).ToList(),
            blocks.Page);
    }

    public async Task<IReadOnlySet<Guid>> FriendIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.Friendships
            .Where(f => f.UserAId == userId || f.UserBId == userId)
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public Task<bool> IsBlockedEitherWayAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken) =>
        db.Blocks.AnyAsync(
            x => (x.BlockerId == userId && x.BlockedId == otherUserId) || (x.BlockerId == otherUserId && x.BlockedId == userId),
            cancellationToken);

    public async Task<IReadOnlySet<Guid>> BlockedEitherWayIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.Blocks
            .Where(x => x.BlockerId == userId || x.BlockedId == userId)
            .Select(x => x.BlockerId == userId ? x.BlockedId : x.BlockerId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    /// <summary>
    /// The pending request, if the caller is its right side. Someone else's request is reported as
    /// nonexistent, so that relations between other users are not revealed.
    /// </summary>
    private async Task<FriendRequest> PendingRequestForAsync(Guid requestId, Func<FriendRequest, bool> isCallersSide, CancellationToken cancellationToken)
    {
        var request = await db.FriendRequests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null || !isCallersSide(request))
        {
            throw new FriendRequestNotFoundException();
        }

        return request.Status == FriendRequestStatus.Pending ? request : throw new FriendRequestAlreadyHandledException();
    }

    private Task<FriendRequest?> PendingAsync(Guid requesterId, Guid addresseeId, CancellationToken cancellationToken) =>
        db.FriendRequests.SingleOrDefaultAsync(
            r => r.RequesterId == requesterId && r.AddresseeId == addresseeId && r.Status == FriendRequestStatus.Pending,
            cancellationToken);

    /// <summary>An account is known to exist by its profile — this module may not read <c>users</c>.</summary>
    private async Task RequireUserExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if ((await profiles.DisplayNamesAsync([userId], cancellationToken)).Count == 0)
        {
            throw new UserNotFoundException();
        }
    }

    private async Task<People> PeopleAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return new People(await profiles.DisplayNamesAsync(ids, cancellationToken), await profiles.AvatarUrlsAsync(ids, cancellationToken));
    }

    private async Task<FriendRequestResponse> ToResponseAsync(FriendRequest request, CancellationToken cancellationToken) =>
        ToResponse(request, await PeopleAsync([request.RequesterId, request.AddresseeId], cancellationToken));

    private static FriendRequestResponse ToResponse(FriendRequest request, People people) => new(
        request.Id,
        request.RequesterId,
        people.Names.GetValueOrDefault(request.RequesterId),
        people.Avatars.GetValueOrDefault(request.RequesterId),
        request.AddresseeId,
        people.Names.GetValueOrDefault(request.AddresseeId),
        people.Avatars.GetValueOrDefault(request.AddresseeId),
        request.Status switch
        {
            FriendRequestStatus.Pending => FriendRequestStatusDto.Pending,
            FriendRequestStatus.Accepted => FriendRequestStatusDto.Accepted,
            FriendRequestStatus.Declined => FriendRequestStatusDto.Declined,
            _ => throw new InvalidOperationException($"Unknown friend request status {request.Status}"),
        },
        request.CreatedAt);

    private sealed record People(IReadOnlyDictionary<Guid, string> Names, IReadOnlyDictionary<Guid, string> Avatars);
}
