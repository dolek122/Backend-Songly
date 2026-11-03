using Microsoft.EntityFrameworkCore;
using Songly.Content.Domain;
using Songly.Content.Persistence;
using Songly.Identity.Contracts;
using Songly.Social.Contracts;

namespace Songly.Content.Posts;

/// <summary>
/// Whose content a user may not see — the single point every read and write path of the module
/// goes through (original <c>hiddenAuthorsFor</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Blocks in either direction</b> (original Claude.md §9): a block that only hid the blocker's
/// view would be closing one's own eyes, while the other side kept following everything. It is
/// enforced here, not only in the social module, because a block that ended the friendship but
/// hid no content let the blocked user keep reading the blocker's posts — and keep reaching them
/// with a comment or a like under their own post, each of which ends in a notification.
/// </para>
/// <para>
/// <b>All suspended accounts</b>, whoever asks (original ADR 0026): a suspension cut the account
/// off, but the harassing posts it was suspended for stayed. Adding them here covers every path at
/// once, and any new path that uses this set gets it for free.
/// </para>
/// <para>
/// <b>Hidden when read, never deleted</b>: blocks are lifted and suspensions expire, and lifting
/// them has to bring everything back. Deleting would make either a heavier penalty than itself.
/// </para>
/// </remarks>
internal sealed class ContentVisibility(ContentDbContext db, ISocialGraph socialGraph, ISuspendedAccounts suspendedAccounts)
{
    /// <summary>
    /// A <see cref="HashSet{T}"/> because the queries filter with it (EF Core sends it as one array
    /// parameter) and the code checks single authors against it. Mostly empty — most users block
    /// nobody, and an empty set must hide nothing.
    /// </summary>
    public async Task<HashSet<Guid>> HiddenAuthorsForAsync(Guid viewerId, CancellationToken cancellationToken)
    {
        var hidden = new HashSet<Guid>(await socialGraph.BlockedEitherWayIdsAsync(viewerId, cancellationToken));
        hidden.UnionWith(await suspendedAccounts.SuspendedUserIdsAsync(cancellationToken));
        return hidden;
    }

    /// <summary>
    /// The post as <paramref name="viewerId"/> sees it — for the write paths (like, share,
    /// comment). Those, not the reads, are the real reason a block exists: each ends in a
    /// notification to the author.
    /// </summary>
    public async Task<Post> VisiblePostAsync(Guid postId, Guid viewerId, CancellationToken cancellationToken) =>
        await VisiblePostAsync(postId, await HiddenAuthorsForAsync(viewerId, cancellationToken), cancellationToken);

    public async Task<Post> VisiblePostAsync(Guid postId, HashSet<Guid> hiddenAuthors, CancellationToken cancellationToken)
    {
        var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == postId, cancellationToken);
        return post is not null && !hiddenAuthors.Contains(post.AuthorId) ? post : throw new PostNotFoundException();
    }
}
