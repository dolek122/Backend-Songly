using System.Transactions;
using Microsoft.Extensions.Logging;

namespace Songly.Common.Persistence;

/// <summary>
/// One database transaction across the DbContexts of several modules — for work where either every
/// module's part happens or none does (creating and deleting an account) — and the work that may
/// happen only once it has committed.
/// </summary>
/// <remarks>
/// <para>
/// An ambient <see cref="TransactionScope"/>: every module's DbContext in it enlists on its own, so
/// a module reacting inside the transaction needs no reference to the publisher's context or
/// transaction. Npgsql hands the same physical connection to the connections opened inside one
/// transaction one after another, so nothing escalates to a distributed transaction (which .NET on
/// Linux does not support). That holds while the work inside is sequential — never two queries in
/// flight at once.
/// </para>
/// <para>
/// Two settings that are easy to get wrong: <c>ReadCommitted</c>, because the default isolation of
/// a <see cref="TransactionScope"/> is <c>Serializable</c>, which in Postgres turns ordinary
/// concurrency into serialization failures; and async flow, without which the transaction does not
/// follow the code across an <c>await</c>.
/// </para>
/// <para>
/// <b>Work after the commit</b> (<see cref="IAfterCommit"/>, ADR 0010) is the counterpart of the
/// original's <c>@TransactionalEventListener(AFTER_COMMIT)</c>: a participant that has to touch
/// something outside the database — delete the file a deleted row pointed to — defers it, and it
/// runs only once the commit has succeeded; after a rollback it never runs. One instance per DI
/// scope, so every handler in the request defers into the transaction that is running.
/// </para>
/// </remarks>
public sealed partial class ModuleTransaction(ILogger<ModuleTransaction> logger) : IAfterCommit
{
    private List<Func<CancellationToken, Task>>? _deferred;

    /// <summary>Runs <paramref name="work"/> in one transaction, commits it, then runs the deferred work.</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        if (_deferred is not null)
        {
            throw new InvalidOperationException("A ModuleTransaction is already running in this scope");
        }

        _deferred = [];
        try
        {
            T result;
            using (var scope = new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                TransactionScopeAsyncFlowOption.Enabled))
            {
                result = await work(cancellationToken);
                scope.Complete();
            }

            // Disposing the completed scope committed it (a failed commit throws, and nothing below
            // runs). There is nothing left to roll back, so deferred work that fails is logged, not
            // thrown: the response must not say the operation failed when it did not.
            foreach (var deferred in _deferred)
            {
                try
                {
                    await deferred(cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    LogDeferredWorkFailed(exception);
                }
            }

            return result;
        }
        finally
        {
            _deferred = null;
        }
    }

    public Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        RunAsync(
            async token =>
            {
                await work(token);
                return true;
            },
            cancellationToken);

    public void Defer(Func<CancellationToken, Task> work) =>
        (_deferred ?? throw new InvalidOperationException("Work can be deferred only inside ModuleTransaction.RunAsync — outside it, nothing would ever run it"))
            .Add(work);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Work deferred until after the commit failed; the transaction stays committed")]
    private partial void LogDeferredWorkFailed(Exception exception);
}
