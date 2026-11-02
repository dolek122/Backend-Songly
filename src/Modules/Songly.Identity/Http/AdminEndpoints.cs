using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Web;
using Songly.Identity.Administration;

namespace Songly.Identity.Http;

/// <summary>
/// Administration (original ADR 0021, 0022, 0023). No authorization here on purpose: everything
/// under <c>/api/v1/admin</c> is admin-only by path (AdminAreaGuard), so a new endpoint in this
/// group is protected the moment it exists.
/// </summary>
internal static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Admin");

        admin.MapGet("/admins", (AdminRoleService service, CancellationToken cancellationToken) => service.AdminsAsync(cancellationToken));

        admin.MapPut("/users/{userId}/admin", async Task<NoContent> (Guid userId, ClaimsPrincipal user, AdminRoleService service, CancellationToken cancellationToken) =>
        {
            await service.GrantAsync(userId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        admin.MapDelete("/users/{userId}/admin", async Task<NoContent> (Guid userId, ClaimsPrincipal user, AdminRoleService service, CancellationToken cancellationToken) =>
        {
            await service.RevokeAsync(userId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        admin.MapGet("/suspensions", (AccountSuspensionService service, CancellationToken cancellationToken) => service.SuspendedAccountsAsync(cancellationToken));

        admin.MapPut("/users/{userId}/suspension", async Task<NoContent> (Guid userId, SuspendAccountRequest request, ClaimsPrincipal user, AccountSuspensionService service, CancellationToken cancellationToken) =>
        {
            await service.SuspendAsync(userId, user.GetUserId(), request, cancellationToken);
            return TypedResults.NoContent();
        });

        // Lifting a suspension is also accepting a pending appeal — one decision, one endpoint.
        admin.MapDelete("/users/{userId}/suspension", async Task<NoContent> (Guid userId, ClaimsPrincipal user, AccountSuspensionService service, CancellationToken cancellationToken) =>
        {
            await service.LiftAsync(userId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        admin.MapPost("/users/{userId}/suspension/appeal/rejection", async Task<NoContent> (Guid userId, ClaimsPrincipal user, AccountSuspensionService service, CancellationToken cancellationToken) =>
        {
            await service.RejectAppealAsync(userId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
