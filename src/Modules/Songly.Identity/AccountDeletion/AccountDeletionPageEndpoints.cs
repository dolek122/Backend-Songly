using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Songly.Common.Validation;
using Songly.Identity.Http;

namespace Songly.Identity.AccountDeletion;

/// <summary>
/// The public account deletion page (original ADR 0043). Its address goes into the "Data safety"
/// form in Google Play.
/// </summary>
/// <remarks>
/// <para>
/// Outside <c>/api/v1</c> and outside the OpenAPI document: a page for a person in a browser, not an
/// operation for the mobile client.
/// </para>
/// <para>
/// <b>No antiforgery token, deliberately.</b> A forged request form can at most send a link to an
/// address the attacker chose — which the attacker can do by typing it in. A forged confirmation
/// needs the token, which the attacker does not have. The CSP header forbids framing the page and
/// loading anything from elsewhere.
/// </para>
/// </remarks>
internal static class AccountDeletionPageEndpoints
{
    private static readonly EmailAttribute EmailFormat = new();

    public static void MapAccountDeletionPages(this IEndpointRouteBuilder endpoints)
    {
        var pages = endpoints.MapGroup(string.Empty).AllowAnonymous().ExcludeFromDescription().DisableAntiforgery();

        pages.MapGet(AccountDeletionPages.StartPath, (IOptions<AccountDeletionOptions> options) =>
            new HtmlPageResult(StatusCodes.Status200OK, AccountDeletionPages.Start(TtlMinutes(options), error: null)));

        // Always the same "check your mailbox" page, whether or not the address has an account.
        pages.MapPost(AccountDeletionPages.StartPath, async ([FromForm] string? email, AccountDeletionService service, IOptions<AccountDeletionOptions> options, CancellationToken cancellationToken) =>
        {
            var normalized = EmailAddresses.Normalize(email);
            if (string.IsNullOrWhiteSpace(normalized) || !EmailFormat.IsValid(normalized))
            {
                return new HtmlPageResult(StatusCodes.Status400BadRequest, AccountDeletionPages.Start(TtlMinutes(options), "Podaj poprawny adres e-mail."));
            }

            await service.RequestDeletionLinkAsync(normalized, cancellationToken);
            return new HtmlPageResult(StatusCodes.Status200OK, AccountDeletionPages.LinkSent(TtlMinutes(options)));
        });

        // Opening the link deletes nothing — it shows a button. Mail scanners and link previews
        // open the addresses in a message without anyone clicking.
        pages.MapGet(AccountDeletionPages.ConfirmPath, async (string? token, AccountDeletionService service, IOptions<AccountDeletionOptions> options, CancellationToken cancellationToken) =>
            await service.IsValidAsync(token, cancellationToken)
                ? new HtmlPageResult(StatusCodes.Status200OK, AccountDeletionPages.Confirm(token!))
                : new HtmlPageResult(StatusCodes.Status404NotFound, AccountDeletionPages.InvalidLink(TtlMinutes(options))));

        pages.MapPost(AccountDeletionPages.ConfirmPath, async ([FromForm] string? token, AccountDeletionService service, IOptions<AccountDeletionOptions> options, CancellationToken cancellationToken) =>
            await service.ConfirmDeletionAsync(token, cancellationToken)
                ? new HtmlPageResult(StatusCodes.Status200OK, AccountDeletionPages.Deleted())
                : new HtmlPageResult(StatusCodes.Status404NotFound, AccountDeletionPages.InvalidLink(TtlMinutes(options))));
    }

    private static int TtlMinutes(IOptions<AccountDeletionOptions> options) => (int)options.Value.Ttl.TotalMinutes;
}
