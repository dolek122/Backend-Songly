using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Songly.Common.Events;
using Songly.Identity.Contracts;
using Songly.Identity.Http;
using Songly.Identity.Persistence;

namespace Songly.Identity.Administration;

/// <summary>
/// Granting and revoking the administrator role through the API (original ADR 0021) instead of a
/// manual <c>UPDATE</c>.
/// </summary>
/// <remarks>
/// <b>The first administrator still has to be created in the database</b>, and that cannot be
/// removed: only an administrator grants the role, so the first one has to come from somewhere.
/// This removes manual SQL as an <i>operating procedure</i>, not as a one-time bootstrap. Every
/// change leaves three traces, each answering another question (original ADR 0031): a warning in
/// the log (ids only, no addresses) — what is happening now; a metric — how much of it; and an
/// entry in the moderation history, through <see cref="AdminRoleChanged"/> — who gave whom that
/// power, and when, durably and through the API.
/// </remarks>
internal sealed partial class AdminRoleService(IdentityDbContext db, AdministrationMetrics metrics, IDomainEvents events, TimeProvider time, ILogger<AdminRoleService> logger)
{
    /// <summary>
    /// The full list, <b>without paging</b> — against the API convention, on purpose. The set is
    /// small by nature and the list exists to audit who holds power; paging would allow missing
    /// someone on page two, which defeats the purpose.
    /// </summary>
    public async Task<AdminListResponse> AdminsAsync(CancellationToken cancellationToken) =>
        new(await db.Users
            .Where(user => user.IsAdmin)
            .OrderBy(user => user.CreatedAt)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken));

    /// <summary>Idempotent: granting the role to someone who has it is not an error.</summary>
    public async Task GrantAsync(Guid targetUserId, Guid requesterId, CancellationToken cancellationToken)
    {
        var target = await db.Users.SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken)
            ?? throw new AdminUserNotFoundException();
        if (target.IsAdmin)
        {
            return;
        }

        target.GrantAdmin();
        await db.SaveChangesAsync(cancellationToken);

        metrics.RoleChange("granted");
        LogGranted(targetUserId, requesterId);
        await events.PublishAsync(new AdminRoleChanged(targetUserId, requesterId, Granted: true, time.GetUtcNow()), cancellationToken);
    }

    /// <summary>
    /// Idempotent towards someone without the role. <b>Not towards oneself</b> — see
    /// <see cref="CannotRevokeOwnAdminException"/>. Takes effect on the target's next request, with
    /// no session to invalidate: the role is read from the database every time (original ADR 0006).
    /// </summary>
    public async Task RevokeAsync(Guid targetUserId, Guid requesterId, CancellationToken cancellationToken)
    {
        if (targetUserId == requesterId)
        {
            throw new CannotRevokeOwnAdminException();
        }

        var target = await db.Users.SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken)
            ?? throw new AdminUserNotFoundException();
        if (!target.IsAdmin)
        {
            return;
        }

        target.RevokeAdmin();
        await db.SaveChangesAsync(cancellationToken);

        metrics.RoleChange("revoked");
        LogRevoked(targetUserId, requesterId);
        await events.PublishAsync(new AdminRoleChanged(targetUserId, requesterId, Granted: false, time.GetUtcNow()), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Administrator role granted to {UserId} by {RequesterId}")]
    private partial void LogGranted(Guid userId, Guid requesterId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Administrator role revoked from {UserId} by {RequesterId}")]
    private partial void LogRevoked(Guid userId, Guid requesterId);
}
