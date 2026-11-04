using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Content.Contracts;
using Songly.Content.Domain;
using Songly.Content.Persistence;

namespace Songly.Content.Posts;

/// <summary>Likes and shares — idempotent both ways: liking twice is one like, unliking what is not there is no error.</summary>
/// <remarks>
/// A new like or share — not a repeated <c>PUT</c> — is published as <see cref="PostLiked"/> or
/// <see cref="PostShared"/> after it is saved. Two simultaneous first likes both see "not there yet";
/// the second insert hits the primary key, and that is a 409, not a 500 (original Claude.md §6).
/// </remarks>
internal sealed class ReactionService(ContentDbContext db, ContentVisibility visibility, IDomainEvents events, TimeProvider time)
{
    public async Task LikeAsync(Guid postId, Guid userId, CancellationToken cancellationToken)
    {
        var post = await visibility.VisiblePostAsync(postId, userId, cancellationToken);
        if (!await db.Likes.AnyAsync(l => l.PostId == postId && l.UserId == userId, cancellationToken))
        {
            var now = time.GetUtcNow();
            db.Likes.Add(new Like(postId, userId, now));
            await db.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(new PostLiked(postId, post.AuthorId, userId, now), cancellationToken);
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
        var post = await visibility.VisiblePostAsync(postId, userId, cancellationToken);
        if (!await db.Shares.AnyAsync(s => s.PostId == postId && s.UserId == userId, cancellationToken))
        {
            var now = time.GetUtcNow();
            db.Shares.Add(new Share(postId, userId, now));
            await db.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(new PostShared(postId, post.AuthorId, userId, now), cancellationToken);
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
