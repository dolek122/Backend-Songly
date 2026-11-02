using System.Transactions;

namespace Songly.Common.Persistence;

/// <summary>
/// One database transaction across the DbContexts of several modules — for work where either
/// every module's part happens or none does (account deletion).
/// </summary>
/// <remarks>
/// <para>
/// An ambient <see cref="TransactionScope"/>: every module's DbContext in it enlists on its own, so
/// a module reacting inside the transaction needs no reference to the publisher's context or
/// transaction. Npgsql hands the same physical connection to the connections opened inside one
/// transaction one after another, so nothing escalates to a distributed transaction (which .NET on
/// Linux does not support). That holds while the work inside the scope is sequential — never two
/// queries in flight at once.
/// </para>
/// <para>
/// Two settings that are easy to get wrong: <c>ReadCommitted</c>, because the default isolation of
/// a <see cref="TransactionScope"/> is <c>Serializable</c>, which in Postgres turns ordinary
/// concurrency into serialization failures; and async flow, without which the transaction does not
/// follow the code across an <c>await</c>.
/// </para>
/// </remarks>
public static class ModuleTransaction
{
    public static TransactionScope Begin() => new(
        TransactionScopeOption.Required,
        new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
        TransactionScopeAsyncFlowOption.Enabled);
}
