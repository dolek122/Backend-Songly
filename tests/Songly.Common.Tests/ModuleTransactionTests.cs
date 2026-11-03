using Microsoft.Extensions.Logging.Abstractions;
using Songly.Common.Persistence;

namespace Songly.Common.Tests;

/// <summary>
/// Work deferred until after the commit (ADR 0010). No database is needed: whether the deferred
/// work runs depends only on whether the transaction scope completed. That the database really
/// commits or rolls back with it is tested end to end by the account deletion tests.
/// </summary>
public sealed class ModuleTransactionTests
{
    private readonly ModuleTransaction _transaction = new(NullLogger<ModuleTransaction>.Instance);
    private readonly List<string> _ran = [];

    [Fact]
    public async Task Deferred_work_runs_after_the_work_and_only_once_it_committed()
    {
        await _transaction.RunAsync(
            _ =>
            {
                _transaction.Defer(_ => Ran("deferred"));
                _ran.Add("work");
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["work", "deferred"], _ran);
    }

    /// <summary>A rollback discards deferred work — a file deleted for a deletion that never happened is gone for good.</summary>
    [Fact]
    public async Task Deferred_work_never_runs_when_the_work_fails()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _transaction.RunAsync(
            _ =>
            {
                _transaction.Defer(_ => Ran("deferred"));
                throw new InvalidOperationException("a module failed");
            },
            TestContext.Current.CancellationToken));

        Assert.Empty(_ran);
    }

    /// <summary>
    /// After the commit there is nothing to roll back: a failure is logged, the rest still runs, and
    /// the caller hears that the operation succeeded — because it did.
    /// </summary>
    [Fact]
    public async Task Failing_deferred_work_does_not_fail_the_committed_transaction()
    {
        await _transaction.RunAsync(
            _ =>
            {
                _transaction.Defer(_ => throw new IOException("storage down"));
                _transaction.Defer(_ => Ran("second"));
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["second"], _ran);
    }

    /// <summary>Outside a running transaction nothing would ever run the work — so deferring fails loudly instead of silently.</summary>
    [Fact]
    public void Deferring_outside_a_transaction_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => _transaction.Defer(_ => Task.CompletedTask));

    [Fact]
    public async Task The_transaction_can_run_again_after_one_finished()
    {
        await _transaction.RunAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken);
        await _transaction.RunAsync(
            _ =>
            {
                _transaction.Defer(_ => Ran("second run"));
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["second run"], _ran);
    }

    private Task Ran(string what)
    {
        _ran.Add(what);
        return Task.CompletedTask;
    }
}
