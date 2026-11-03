namespace Songly.Community.Contracts;

/// <summary>
/// Descriptive data of a community for other modules — an e-mail about a moderation decision has to
/// name the community, while domain events carry only its id (original ADR 0013).
/// </summary>
public interface ICommunityDirectory
{
    /// <summary><c>null</c> when the community does not exist (deleted in the meantime, for one).</summary>
    Task<string?> NameOfAsync(Guid communityId, CancellationToken cancellationToken);
}
