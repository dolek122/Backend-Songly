using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;
using Songly.Common.Validation;
using Songly.Common.Web;

namespace Songly.IntegrationTests;

/// <summary>
/// Test-only endpoints that exercise shared infrastructure independently of any real feature.
/// Plugged in by <see cref="SonglyApiFactory"/>, never part of the app.
/// </summary>
internal sealed class ProbeModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Error-format probes are anonymous: they test the error handling, not authorization.
        var probe = endpoints.MapGroup("/probe").AllowAnonymous();

        probe.MapGet("/api-exception", () => { throw new ProbeConflictException(); });
        probe.MapGet("/unexpected", () => { throw new InvalidOperationException("boom"); });
        probe.MapPost("/body", (ProbeRequest request) => Results.Ok(request));
        probe.MapGet("/query", (string name) => Results.Ok(name));
        probe.MapGet("/items/{id}", (Guid id) => Results.Ok(id));
        probe.MapPost("/validated", (ValidatedProbeRequest request) => Results.Ok(request));
        probe.MapGet("/json-shape", () => new ProbeShape(
            new DateTimeOffset(2026, 11, 2, 13, 30, 15, 123, TimeSpan.FromHours(1)),
            ProbeStatus.ModeratorGranted));

        // No authorization metadata at all: what every new endpoint looks like by default.
        endpoints.MapGet("/probe-secured/whoami", (ClaimsPrincipal user) => user.GetUserId());

        // Admin area probes. The second one tries — and must fail — to opt out of the admin rule.
        endpoints.MapGet("/api/v1/admin/probe", () => "admin");
        endpoints.MapGet("/api/v1/admin/probe-anonymous", () => "admin").AllowAnonymous();
    }
}

internal sealed record ProbeRequest(string Name);

internal sealed record ValidatedProbeRequest
{
    [NotBlank]
    [Email]
    public string? Email { get; init; }

    [NotBlank]
    [Size(8, 72)]
    [MaxUtf8Bytes(72)]
    public string? Password { get; init; }
}

internal sealed record ProbeShape(DateTimeOffset At, ProbeStatus Status);

internal enum ProbeStatus
{
    Member,
    ModeratorGranted,
}

internal sealed class ProbeConflictException()
    : ApiException(StatusCodes.Status409Conflict, "PROBE_CONFLICT", "Konflikt testowy");
