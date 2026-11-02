using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Identity.Contracts;
using Songly.Identity.Persistence;

namespace Songly.Identity.AccountDeletion;

/// <summary>
/// Deletes an account and all its data — without checking who asks. Shared by the two ways to
/// delete an account (password in the app, link from the e-mail), which differ only in the proof of
/// identity. Two copies of the deletion itself would drift apart at the first new table with a
/// foreign key to <c>users</c>, and the first sign would be a foreign-key error in production.
/// </summary>
internal sealed class AccountEraser(IdentityDbContext db, IDomainEvents events)
{
    /// <summary>
    /// Must run inside the caller's <c>ModuleTransaction</c>: the other modules delete their data in
    /// it, synchronously, before the <c>users</c> row goes.
    /// </summary>
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (Transaction.Current is null)
        {
            throw new InvalidOperationException("Account deletion must run inside a ModuleTransaction");
        }

        await events.PublishAsync(new UserAccountDeletionRequested(userId), cancellationToken);

        // This module's own tables reference users too. They are deleted directly — the event exists
        // so that no module reaches into another module's tables, not to talk to itself.
        await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.AccountDeletionTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
