namespace Songly.Identity.Contracts;

/// <summary>
/// An account is being deleted (GDPR, original Claude.md §9). Every module that keeps data per user
/// handles it by deleting that data.
/// </summary>
/// <remarks>
/// Published <b>inside</b> the deletion's transaction and <b>before</b> the <c>users</c> row goes:
/// other modules' tables reference <c>users</c> without <c>ON DELETE CASCADE</c> — a cascade in the
/// database would delete another module's rows behind its back — so they have to clean up first,
/// or the database refuses the delete. A failing handler rolls the whole deletion back.
/// A module with per-user data that does not handle this event shows up as a foreign-key error
/// the first time an account is deleted.
/// </remarks>
public sealed record UserAccountDeletionRequested(Guid UserId);
