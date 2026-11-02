using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Songly.Identity.Domain;
using Songly.Identity.Http;
using Songly.Identity.Persistence;

namespace Songly.Identity.Administration;

/// <summary>
/// Platform-wide account suspension (original ADR 0022) and appeals against it (ADR 0023).
/// </summary>
/// <remarks>
/// Enforcement does not live here: a suspended account gets the <c>SUSPENDED</c> role instead of
/// <c>USER</c> on every request, and every endpoint requires <c>USER</c> unless it says otherwise.
/// This service only changes the state authorization reads.
/// </remarks>
internal sealed partial class AccountSuspensionService(
    IdentityDbContext db,
    AdministrationMetrics metrics,
    TimeProvider time,
    ILogger<AccountSuspensionService> logger)
{
    /// <summary>
    /// Suspends an account, or re-decides an ongoing suspension.
    /// </summary>
    /// <remarks>
    /// Two prohibitions, carried over from the community moderation rules: <b>not oneself</b> (the
    /// administrator would lose the endpoint that undoes it) and <b>not another administrator</b>
    /// (two admins could switch each other off). Notifying the suspended user (by e-mail, with the
    /// term but never the reason) belongs to the notification module and arrives with it.
    /// </remarks>
    public async Task SuspendAsync(Guid targetUserId, Guid requesterId, SuspendAccountRequest request, CancellationToken cancellationToken)
    {
        if (targetUserId == requesterId)
        {
            throw new CannotSuspendSelfException();
        }

        var target = await db.Users.SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken)
            ?? throw new AdminUserNotFoundException();
        if (target.IsAdmin)
        {
            throw new CannotSuspendAdminException();
        }

        var now = time.GetUtcNow();
        var expiresAt = request.DurationDays is { } days ? now.AddDays(days) : (DateTimeOffset?)null;
        target.Suspend(request.Reason!, now, expiresAt);
        await db.SaveChangesAsync(cancellationToken);

        metrics.Suspension("suspended");
        LogSuspended(targetUserId, requesterId, expiresAt is null);
    }

    /// <summary>
    /// Lifts a suspension. Idempotent: lifting nothing is not an error.
    /// </summary>
    /// <remarks>
    /// Lifting a suspension with a pending appeal <b>is</b> accepting the appeal — one decision, so
    /// there is no separate "accept" endpoint, and no state "appeal accepted but still suspended".
    /// </remarks>
    public async Task LiftAsync(Guid targetUserId, Guid requesterId, CancellationToken cancellationToken)
    {
        var target = await db.Users.SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken)
            ?? throw new AdminUserNotFoundException();
        if (target.SuspendedAt is null)
        {
            return;
        }

        var acceptsAppeal = target.IsSuspendedAt(time.GetUtcNow()) && target.HasPendingAppeal;
        target.LiftSuspension();
        await db.SaveChangesAsync(cancellationToken);

        metrics.Suspension("lifted");
        LogLifted(targetUserId, requesterId, acceptsAppeal);
    }

    /// <summary>
    /// <b>Active</b> suspensions only — an expired one counts nowhere, so listing it would suggest a
    /// sanction that does not exist. No paging: a list to review, not a stream of data.
    /// </summary>
    public async Task<IReadOnlyList<SuspendedAccountResponse>> SuspendedAccountsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var suspended = await db.Users
            .Where(user => user.SuspendedAt != null && (user.SuspensionExpiresAt == null || user.SuspensionExpiresAt > now))
            .OrderByDescending(user => user.SuspendedAt)
            .ToListAsync(cancellationToken);

        return suspended
            .Select(user => new SuspendedAccountResponse(
                user.Id,
                user.SuspensionReason,
                user.SuspendedAt,
                user.SuspensionExpiresAt,
                user.SuspensionAppealText,
                user.SuspensionAppealedAt,
                ToApi(user.SuspensionAppealStatus)))
            .ToList();
    }

    public async Task<SuspensionResponse> OwnSuspensionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsSuspendedAt(time.GetUtcNow()))
        {
            return SuspensionResponse.NotSuspended;
        }

        return new SuspensionResponse(
            true,
            user.SuspensionReason,
            user.SuspendedAt,
            user.SuspensionExpiresAt,
            ToApi(user.SuspensionAppealStatus),
            user.SuspensionAppealedAt);
    }

    /// <summary>
    /// An appeal against one's <b>own</b> suspension: only while it is active, and once per
    /// suspension. The limit comes from the data structure, not from a counter. The text is read
    /// only in the administrators' list (pull channel); notifying the administrators that there is
    /// something to read arrives with the notification module.
    /// </summary>
    public async Task AppealAsync(Guid userId, SuspensionAppealRequest request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsSuspendedAt(now))
        {
            throw new NoSuspensionToAppealException();
        }

        if (user.HasAppeal)
        {
            throw new SuspensionAppealAlreadySubmittedException();
        }

        user.SubmitAppeal(request.Text!, now);
        await db.SaveChangesAsync(cancellationToken);

        metrics.Suspension("appeal-submitted");
        LogAppealed(userId);
    }

    /// <summary>
    /// The suspension stays, and for this suspension the case is closed. Requires a <b>pending</b>
    /// appeal on an <b>active</b> suspension — an appeal against an expired one is moot.
    /// </summary>
    public async Task RejectAppealAsync(Guid targetUserId, Guid requesterId, CancellationToken cancellationToken)
    {
        var target = await db.Users.SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken);
        if (target is null || !target.IsSuspendedAt(time.GetUtcNow()) || !target.HasPendingAppeal)
        {
            throw new SuspensionAppealNotFoundException();
        }

        target.RejectAppeal();
        await db.SaveChangesAsync(cancellationToken);

        metrics.Suspension("appeal-rejected");
        LogAppealRejected(targetUserId, requesterId);
    }

    private static AppealStatus? ToApi(SuspensionAppealStatus? status) => status switch
    {
        SuspensionAppealStatus.Pending => AppealStatus.Pending,
        SuspensionAppealStatus.Rejected => AppealStatus.Rejected,
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account {UserId} suspended by {RequesterId} (indefinitely: {Indefinitely})")]
    private partial void LogSuspended(Guid userId, Guid requesterId, bool indefinitely);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suspension of account {UserId} lifted by {RequesterId} (appeal accepted: {AppealAccepted})")]
    private partial void LogLifted(Guid userId, Guid requesterId, bool appealAccepted);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} appealed its suspension")]
    private partial void LogAppealed(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suspension appeal of account {UserId} rejected by {RequesterId}")]
    private partial void LogAppealRejected(Guid userId, Guid requesterId);
}
