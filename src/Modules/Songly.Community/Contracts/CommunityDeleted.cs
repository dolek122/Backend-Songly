namespace Songly.Community.Contracts;

/// <summary>
/// A community is being deleted — modules keeping rows tied to it delete them (content: the posts
/// published in it).
/// </summary>
/// <remarks>
/// Published <b>inside</b> the transaction and <b>before</b> the <c>communities</c> row goes, the
/// same as <c>UserAccountDeletionRequested</c>: <c>posts.community_id</c> references the community
/// without <c>ON DELETE CASCADE</c> — a cascade in the database would cut across the module
/// boundary and delete content's rows without content knowing (original V13 migration) — so the
/// handlers must have finished their part before the delete, or the database rejects it. Either
/// everything goes or nothing does.
/// </remarks>
public sealed record CommunityDeleted(Guid CommunityId);
