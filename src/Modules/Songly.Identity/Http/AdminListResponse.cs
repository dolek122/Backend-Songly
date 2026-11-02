namespace Songly.Identity.Http;

/// <summary>
/// Ids only, no names: identity must not depend on profile (profile depends on identity — a cycle).
/// The client adds names from the profile module.
/// </summary>
internal sealed record AdminListResponse(IReadOnlyList<Guid> UserIds);
