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

internal enum ReportReasonDto
{
    Spam,
    Harassment,
    HateSpeech,
    IllegalContent,
    Other,
}

internal sealed record ReportPostRequest
{
    [NotNull]
    public ReportReasonDto? Reason { get; init; }

    [Size(0, 500)]
    public string? Details { get; init; }
}

/// <summary>
/// An entry of the moderation queue: the report together with its post — the moderator should not
/// have to ask for the content separately.
/// </summary>
/// <remarks>
/// <para>
/// <c>reportCount</c> is how many independent people reported the post — weightier than the report
/// itself: one report can be malicious, a dozen hardly.
/// </para>
/// <para>
/// <c>reporterUpheldCount</c> and <c>reporterDismissedCount</c> are this person's accuracy <b>in this
/// community</b> and <b>within the history window</b> (original ADR 0025, 0027, 0030) — a weight
/// given to the moderator, not subtracted for them. Only both together mean anything: "5 upheld, 1
/// dismissed" reads differently from "0 and 1". The community, because a moderator has no right to
/// learn what the reporter did in other communities (the allowance counts globally); the window,
/// because the same numbers drive the penalty, and a number without a time scale is not a weight a
/// person can judge.
/// </para>
/// </remarks>
internal sealed record PostReportResponse(
    Guid Id,
    ReportReasonDto Reason,
    string? Details,
    Guid ReporterId,
    string? ReporterDisplayName,
    DateTimeOffset CreatedAt,
    long ReportCount,
    int ReporterUpheldCount,
    int ReporterDismissedCount,
    PostResponse Post);
