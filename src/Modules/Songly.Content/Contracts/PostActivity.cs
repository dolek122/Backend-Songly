namespace Songly.Content.Contracts;

/// <summary>A new like — not a repeated <c>PUT</c> of one that is there already.</summary>
public sealed record PostLiked(Guid PostId, Guid PostAuthorId, Guid LikerId, DateTimeOffset OccurredAt);

/// <summary>A new comment under a post.</summary>
public sealed record PostCommented(Guid PostId, Guid PostAuthorId, Guid CommentId, Guid CommentAuthorId, DateTimeOffset OccurredAt);

/// <summary>A new share — not a repeated <c>PUT</c> of one that is there already.</summary>
public sealed record PostShared(Guid PostId, Guid PostAuthorId, Guid SharerId, DateTimeOffset OccurredAt);
