namespace Songly.Profile.Profiles;

/// <summary>
/// The name a profile has until its owner picks one (original ADR 0046): "Użytkownik 123456".
/// </summary>
/// <remarks>
/// <para>
/// It used to be the part of the e-mail address before the "@" — public, searchable, and for most
/// people their first and last name: half of their address handed to anyone who opened the
/// profile or typed a fragment of it into the user search. Existing profiles were not renamed: a
/// migration cannot tell a name never changed from one deliberately set to the login.
/// </para>
/// <para>
/// Six digits rather than four: names are not unique, but with a few thousand accounts on the
/// default name a shorter suffix would fill the search with lists of identical-looking people.
/// The number needs no cryptographic randomness — it identifies nobody and protects nothing.
/// </para>
/// </remarks>
internal static class DefaultDisplayName
{
    public const string Prefix = "Użytkownik";

    public static string Generate() => $"{Prefix} {Random.Shared.Next(1_000_000):D6}";
}
