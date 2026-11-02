using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;
using Songly.Common.Web;

namespace Songly.IntegrationTests;

/// <summary>
/// Test-only endpoints that exercise the shared web infrastructure before any real module has
/// endpoints of its own. Plugged in by <see cref="SonglyApiFactory"/>, never part of the app.
/// </summary>
internal sealed class ProbeModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var probe = endpoints.MapGroup("/probe");

        probe.MapGet("/api-exception", () => { throw new ProbeConflictException(); });
        probe.MapGet("/unexpected", () => { throw new InvalidOperationException("boom"); });
        probe.MapPost("/body", (ProbeRequest request) => Results.Ok(request));
        probe.MapGet("/query", (string name) => Results.Ok(name));
        probe.MapGet("/items/{id}", (Guid id) => Results.Ok(id));
        probe.MapGet("/json-shape", () => new ProbeShape(
            new DateTimeOffset(2026, 11, 2, 13, 30, 15, 123, TimeSpan.FromHours(1)),
            ProbeStatus.ModeratorGranted));
    }
}

internal sealed record ProbeRequest(string Name);

internal sealed record ProbeShape(DateTimeOffset At, ProbeStatus Status);

internal enum ProbeStatus
{
    Member,
    ModeratorGranted,
}

internal sealed class ProbeConflictException()
    : ApiException(StatusCodes.Status409Conflict, "PROBE_CONFLICT", "Konflikt testowy");
