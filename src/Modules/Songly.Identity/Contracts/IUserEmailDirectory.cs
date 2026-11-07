namespace Songly.Identity.Contracts;

/// <summary>
/// Users' e-mail addresses for the modules that send them mail (original ADR 0013). Until then only
/// identity knew an address, and every mail — verification, password reset — left from it.
/// </summary>
/// <remarks>
/// <b>An address is sensitive.</b> A consumer gets it only to hand it to <c>IMailSender</c>: it must not
/// log it (identify users by id) nor keep it on its side.
/// </remarks>
public interface IUserEmailDirectory
{
    /// <summary>The addresses of the given users. Ids without an account (deleted in the meantime) are simply absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> EmailsOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
