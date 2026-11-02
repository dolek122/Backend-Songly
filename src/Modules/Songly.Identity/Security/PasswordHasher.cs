namespace Songly.Identity.Security;

/// <summary>bcrypt, cost 10 — the same algorithm and cost as the original backend.</summary>
/// <remarks>
/// Hashes written by the original (Spring Security's <c>BCryptPasswordEncoder</c>, <c>$2a$</c>)
/// verify here unchanged, so the two backends can share a database.
/// </remarks>
internal sealed class PasswordHasher
{
    private const int WorkFactor = 10;

    /// <summary>
    /// Compared against when there is no account (original ADR 0041). Computed once, with the same
    /// cost as real hashes, so that "no such account" costs exactly as much as "wrong password".
    /// </summary>
    private readonly string _unknownAccountHash = BCrypt.Net.BCrypt.HashPassword(SecureTokens.Generate(), WorkFactor);

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    /// <summary>Spends the time of a real verification and always fails.</summary>
    public void VerifyAgainstUnknownAccount(string password) => BCrypt.Net.BCrypt.Verify(password, _unknownAccountHash);
}
