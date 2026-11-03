namespace Songly.Community.Domain;

internal sealed class CommunityMember
{
    // For EF Core.
    private CommunityMember()
    {
    }

    public CommunityMember(Guid communityId, Guid userId, CommunityRole role, DateTimeOffset joinedAt)
    {
        CommunityId = communityId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid CommunityId { get; private set; }

    public Guid UserId { get; private set; }

    public CommunityRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public void ChangeRole(CommunityRole role) => Role = role;
}
