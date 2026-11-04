using Songly.Common.Validation;

namespace Songly.Content.Http;

/// <summary>
/// <c>imageKey</c> is optional and comes from an earlier <c>POST /api/v1/posts/image</c>. Two steps
/// instead of one multipart request, because uploading a file and publishing text behave
/// differently: the file takes long and is retried, the text goes at once. Glued together, a
/// failed publication would make the client send the image again.
/// </summary>
internal sealed record CreatePostRequest
{
    [NotBlank]
    [Size(0, 1000)]
    public string? Content { get; init; }

    [Size(0, 512)]
    public string? ImageKey { get; init; }
}

/// <summary>The result of an upload: <c>key</c> comes back in <see cref="CreatePostRequest.ImageKey"/>, <c>url</c> lets the client show a preview before the post exists.</summary>
internal sealed record PostImageResponse(string Key, string Url);

internal sealed record CreateCommentRequest
{
    [NotBlank]
    [Size(0, 500)]
    public string? Content { get; init; }
}

/// <summary>
/// <c>communityId</c> is <c>null</c> for an ordinary post. <c>imageUrl</c> is <c>null</c> without an
/// image — a ready address, not the key: the key is a detail of our storage the client has no
/// reason to know or keep.
/// </summary>
internal sealed record PostResponse(
    Guid Id,
    Guid AuthorId,
    string? AuthorDisplayName,
    string? AuthorAvatarUrl,
    string Content,
    string? ImageUrl,
    DateTimeOffset CreatedAt,
    Guid? CommunityId,
    long LikeCount,
    bool LikedByMe,
    long CommentCount,
    long ShareCount,
    bool SharedByMe);

internal sealed record CommentResponse(
    Guid Id,
    Guid PostId,
    Guid AuthorId,
    string? AuthorDisplayName,
    string? AuthorAvatarUrl,
    string Content,
    DateTimeOffset CreatedAt);
