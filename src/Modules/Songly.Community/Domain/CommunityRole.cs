namespace Songly.Community.Domain;

/// <summary>
/// A role in a community. Moderators exist so that moderation stops being one person's bottleneck;
/// the owner is still one person, and only they hand out roles — otherwise a moderator could demote
/// the owner and take the community over.
/// </summary>
internal enum CommunityRole
{
    Owner,
    Moderator,
    Member,
}

internal static class CommunityRoles
{
    /// <summary>
    /// The roles that moderate. One place, because the same list answers two questions — "does this
    /// person moderate" and "whom to notify about a report" — and a drift between them would show as
    /// a moderator without notifications, or notifications for someone who can do nothing about them.
    /// </summary>
    public static readonly CommunityRole[] Moderating = [CommunityRole.Owner, CommunityRole.Moderator];

    public static bool CanModerate(this CommunityRole role) => Moderating.Contains(role);
}
