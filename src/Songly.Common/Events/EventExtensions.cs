using Microsoft.Extensions.DependencyInjection;

namespace Songly.Common.Events;

public static class EventExtensions
{
    public static IServiceCollection AddSonglyEvents(this IServiceCollection services) =>
        services.AddScoped<IDomainEvents, DomainEvents>();
}
