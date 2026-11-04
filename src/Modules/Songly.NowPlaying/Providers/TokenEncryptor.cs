using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Songly.Common.Configuration;
using Songly.NowPlaying.Status;

namespace Songly.NowPlaying.Providers;

/// <summary>
/// AES-256-GCM for the providers' tokens: a random 12-byte nonce, then the ciphertext, then the
/// 16-byte tag, all in Base64 — the layout the original's <c>Cipher("AES/GCM/NoPadding")</c> writes,
/// so a row written by either backend decrypts in the other.
/// </summary>
/// <remarks>GCM authenticates as well as encrypts: a token changed in the database fails to decrypt instead of becoming another token.</remarks>
internal sealed class TokenEncryptor(IOptions<NowPlayingOptions> options)
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key = Convert.FromBase64String(options.Value.TokenEncryptionKey);

    public string Encrypt(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[NonceBytes + data.Length + TagBytes];
        var nonce = result.AsSpan(0, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, data, result.AsSpan(NonceBytes, data.Length), result.AsSpan(NonceBytes + data.Length, TagBytes));
        return Convert.ToBase64String(result);
    }

    public string Decrypt(string encoded)
    {
        var bytes = Convert.FromBase64String(encoded);
        var length = bytes.Length - NonceBytes - TagBytes;
        var plaintext = new byte[length];
        using var aes = new AesGcm(_key, TagBytes);
        aes.Decrypt(bytes.AsSpan(0, NonceBytes), bytes.AsSpan(NonceBytes, length), bytes.AsSpan(NonceBytes + length, TagBytes), plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}

/// <summary>The key must be AES-256, and the development key from the repository is refused where real secrets are required.</summary>
internal sealed class TokenEncryptionKeyValidator(IConfiguration configuration) : IValidateOptions<NowPlayingOptions>
{
    public ValidateOptionsResult Validate(string? name, NowPlayingOptions options)
    {
        var setting = $"{NowPlayingOptions.Section}:TokenEncryptionKey";
        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.TokenEncryptionKey);
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail($"{setting} must be Base64");
        }

        if (key.Length != 32)
        {
            return ValidateOptionsResult.Fail($"{setting} must be 32 bytes in Base64 (AES-256)");
        }

        var productionRequired = configuration.GetValue<bool>(DevSecrets.ProductionRequiredSetting);
        return DevSecrets.Reject(productionRequired, Encoding.UTF8.GetString(key), setting) is { } error
            ? ValidateOptionsResult.Fail(error)
            : ValidateOptionsResult.Success;
    }
}
