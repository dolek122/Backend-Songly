using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Songly.Identity.Security;

/// <summary>
/// Generating and hashing bearer tokens (refresh tokens, and later e-mail and reset links).
/// </summary>
/// <remarks>
/// <para>
/// One place, because every path must follow exactly the same rule: the token leaves the server
/// once, and only its hash is stored. Two diverging copies of this logic would be a security bug
/// no functional test notices.
/// </para>
/// <para>
/// Plain SHA-256, no salt or stretching, is <b>enough</b> here, unlike for passwords: the token is
/// 256 random bits, so there is nothing to guess with a dictionary or a rainbow table. The hash
/// protects against a leaked database, not against guessing. The encoding (standard Base64 of the
/// digest) matches the original backend, so the stored hashes are interchangeable.
/// </para>
/// </remarks>
internal static class SecureTokens
{
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
