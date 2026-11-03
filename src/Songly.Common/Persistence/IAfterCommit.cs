namespace Songly.Common.Persistence;

/// <summary>
/// Defers work until the running <see cref="ModuleTransaction"/> commits; a rollback discards it.
/// </summary>
/// <remarks>
/// For whatever lives outside the database and must not change before the database says so — a
/// file deleted too early leaves, after a rollback, a row pointing at nothing (original Claude.md
/// §9). Rows go inside the transaction, files after it.
/// </remarks>
public interface IAfterCommit
{
    /// <exception cref="InvalidOperationException">No <see cref="ModuleTransaction"/> is running in this scope.</exception>
    void Defer(Func<CancellationToken, Task> work);
}
