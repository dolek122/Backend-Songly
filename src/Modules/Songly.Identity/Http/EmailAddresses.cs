namespace Songly.Identity.Http;

/// <summary>
/// One form of an e-mail address in the whole module (original ADR 0042): trimmed, lower case.
/// </summary>
/// <remarks>
/// <para>
/// Normalized at the boundary, while the request is deserialized — so validation already sees
/// the normalized form (a space added by keyboard autocompletion does not end in a 400), and no
/// service can forget to normalize before the next query by address.
/// </para>
/// <para>
/// Lower case although RFC 5321 allows a case-sensitive local part: no common mail provider uses
/// that, and a phone keyboard capitalizes the first letter on its own. Without this,
/// "Jan@gmail.com" at registration and "jan@gmail.com" at login are two different accounts, and
/// the second login ends with "wrong password".
/// </para>
/// </remarks>
internal static class EmailAddresses
{
    public static string? Normalize(string? email) => email?.Trim().ToLowerInvariant();
}
