namespace Songly.Common.Events;

/// <summary>A module's reaction to a domain event published by another module (or its own).</summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : notnull
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
