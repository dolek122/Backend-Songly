using Serilog.Core;
using Serilog.Events;

namespace Songly.Api;

/// <summary>
/// Removes ASP.NET Core's own <c>RequestId</c> from log events.
/// </summary>
/// <remarks>
/// The framework opens its logging scope before any middleware runs, so its <c>RequestId</c> is
/// Kestrel's connection-based identifier, not the one returned to the client in
/// <c>X-Request-Id</c>. Two different identifiers under nearly the same name (<c>RequestId</c>
/// next to our <c>requestId</c>) is a trap during an incident: searching by the one the user
/// quoted must find every line, and nothing else.
/// </remarks>
internal sealed class DropFrameworkRequestIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        => logEvent.RemovePropertyIfPresent("RequestId");
}
