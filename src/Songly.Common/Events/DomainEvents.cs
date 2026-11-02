using Microsoft.Extensions.DependencyInjection;

namespace Songly.Common.Events;

internal sealed class DomainEvents(IServiceProvider services) : IDomainEvents
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : notnull
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync(domainEvent, cancellationToken);
        }
    }
}
