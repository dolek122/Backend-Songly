namespace Songly.Common.Events;

/// <summary>
/// Publishes a domain event to every registered <see cref="IDomainEventHandler{TEvent}"/>,
/// synchronously, in the publisher's DI scope.
/// </summary>
/// <remarks>
/// <para>
/// When handlers run is decided by where the publisher publishes, not by a flag on the handler —
/// the .NET counterpart of the original's two listener kinds:
/// </para>
/// <list type="bullet">
/// <item>Inside a <c>ModuleTransaction</c>, before the publisher's own change: handlers are part of
/// the transaction and a failing handler rolls back everything (Spring's plain
/// <c>@EventListener</c>). Account deletion works this way — every module deletes its rows before
/// the <c>users</c> row, which their foreign keys point to.</item>
/// <item>After the publisher saved: handlers react to a committed fact and cannot undo it
/// (<c>@TransactionalEventListener(AFTER_COMMIT)</c>).</item>
/// </list>
/// <para>
/// Handlers run one after another, in registration order, and exceptions propagate to the
/// publisher. An event is a fact the publisher states; modules learn about each other only
/// through these and the Contracts interfaces.
/// </para>
/// </remarks>
public interface IDomainEvents
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : notnull;
}
