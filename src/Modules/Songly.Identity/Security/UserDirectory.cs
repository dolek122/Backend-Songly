using Microsoft.EntityFrameworkCore;
using Songly.Identity.Contracts;
using Songly.Identity.Persistence;

namespace Songly.Identity.Security;

/// <summary>Addresses and administrators, read for other modules — one query per call, whatever the number of ids.</summary>
internal sealed class UserDirectory(IdentityDbContext db) : IUserEmailDirectory, IAdminDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> EmailsOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Users.Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.Email, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> AdminIdsAsync(CancellationToken cancellationToken) =>
        (await db.Users.Where(user => user.IsAdmin).Select(user => user.Id).ToListAsync(cancellationToken)).ToHashSet();
}
