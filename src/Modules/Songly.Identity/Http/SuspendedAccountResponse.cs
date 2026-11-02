namespace Songly.Identity.Http;

/// <summary>
/// A row of the administrators' list of suspended accounts — which is also the appeal queue, and
/// the <b>only</b> place where an appeal's text is read. No e-mail address: the list shows the
/// moderation state, it is not a directory of contact data.
/// </summary>
internal sealed record SuspendedAccountResponse(
    Guid UserId,
    string? Reason,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset? ExpiresAt,
    string? AppealText,
    DateTimeOffset? AppealedAt,
    AppealStatus? AppealStatus);
