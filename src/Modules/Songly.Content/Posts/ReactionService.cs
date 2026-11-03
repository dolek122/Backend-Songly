using Microsoft.EntityFrameworkCore;
using Songly.Content.Domain;
using Songly.Content.Persistence;

namespace Songly.Content.Posts;

/// <summary>Likes and shares — idempotent both ways: liking twice is one like, unliking what is not there is no error.</summary>
/// <remarks>
/// The original publishes <c>PostLikedEvent</c> and <c>PostSharedEvent</c> for a new like or share
/// (not for a repeated <c>PUT</c>); their consumers are the notification and analytics modules, and
/// the events arrive with the first of them. Two simultaneous first likes both see "not there yet";
/// the second insert hits the primary key, and that is a 409, not a 500 (original Claude.md §6).
/// </remarks>
internal sealed class ReactionService(ContentDbContext db, ContentVisibility visibility, TimeProvider time)
{
    public async Task LikeAsync(Guid postId, Guid userId, CancellationToken cancellationToken)
    {
        await visibility.VisiblePostAsync(postId, userId, cancellationToken);
        if (!await db.Likes.AnyAsync(l => l.PostId == postId && l.UserId == userId, cancellationToken))
        {
            db.Likes.Add(new Like(postId, userId, time.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Deliberately does <b>not</b> check the block, though liking does: someone who blocks a user
    /// after liking their post must be able to take the like back. A block cuts one off from someone
    /// else's content; it does not freeze one's own traces on it.
    /// </summary>
    public async Task UnlikeAsync(Guid postId, Guid userId, CancellationToken cancellationToken)
    {
        await RequirePostExistsAsync(postId, cancellationToken);
        await db.Likes.Where(l => l.PostId == postId && l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ShareAsync(Guid postId, Guid userId, CancellationToken cancellationToken)
    {
        await visibility.VisiblePostAsync(postId, userId, cancellationToken);
        if (!await db.Shares.AnyAsync(s => s.PostId == postId && s.UserId == userId, cancellationToken))
        {
            db.Shares.Add(new Share(postId, userId, time.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>No block check, for the same reason as <see cref="UnlikeAsync"/>.</summary>
    public async Task UnshareAsync(Guid postId, Guid userId, CancellationToken cancellationToken)
    {
        await RequirePostExistsAsync(postId, cancellationToken);
        await db.Shares.Where(s => s.PostId == postId && s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task RequirePostExistsAsync(Guid postId, CancellationToken cancellationToken)
    {
        if (!await db.Posts.AnyAsync(p => p.Id == postId, cancellationToken))
        {
            throw new PostNotFoundException();
        }
    }
}
