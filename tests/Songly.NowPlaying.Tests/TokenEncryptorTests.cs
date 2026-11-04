using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Songly.NowPlaying.Providers;
using Songly.NowPlaying.Status;

namespace Songly.NowPlaying.Tests;

public sealed class TokenEncryptorTests
{
    private const string DevKey = "ZGV2LW9ubHktdG9rZW4tZW5jcnlwdGlvbi1rZXktMzI=";

    private readonly TokenEncryptor _encryptor = Encryptor(DevKey);

    [Fact]
    public void A_token_survives_the_round_trip()
    {
        Assert.Equal("spotify-access-token", _encryptor.Decrypt(_encryptor.Encrypt("spotify-access-token")));
    }

    /// <summary>A fresh nonce every time: the same token encrypted twice must not give the same text, or equal ciphertexts would reveal equal tokens.</summary>
    [Fact]
    public void Every_encryption_uses_a_fresh_nonce()
    {
        Assert.NotEqual(_encryptor.Encrypt("token"), _encryptor.Encrypt("token"));
    }

    /// <summary>
    /// The layout of the original (nonce, ciphertext, tag — what Java's <c>AES/GCM/NoPadding</c> writes
    /// after the nonce): a value made outside .NET with the same key decrypts, so rows written by either
    /// backend stay readable by the other. Made with Python's <c>cryptography</c>, nonce 00…0b.
    /// </summary>
    [Fact]
    public void A_value_in_the_originals_layout_decrypts()
    {
        Assert.Equal("spotify-access-token", _encryptor.Decrypt("AAECAwQFBgcICQoLAiZstxpfOGIACrXh5PWH34SFCkh+cuQOVzYs8xSphPlco4RN"));
    }

    /// <summary>GCM authenticates: a token changed in the database fails instead of becoming another token.</summary>
    [Fact]
    public void A_tampered_value_does_not_decrypt()
    {
        var bytes = Convert.FromBase64String(_encryptor.Encrypt("token"));
        bytes[14] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.Decrypt(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Another_key_does_not_decrypt()
    {
        var other = Encryptor(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt(_encryptor.Encrypt("token")));
    }

    [Theory]
    [InlineData("not base64!", false, false)]
    [InlineData("c2hvcnQ=", false, false)]
    [InlineData(DevKey, false, true)]
    [InlineData(DevKey, true, false)]
    public void The_key_is_validated(string key, bool productionSecretsRequired, bool valid)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Songly:Security:ProductionSecretsRequired"] = productionSecretsRequired.ToString() })
            .Build();

        var result = new TokenEncryptionKeyValidator(configuration).Validate(null, new NowPlayingOptions { TokenEncryptionKey = key });

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void A_real_key_is_accepted_where_real_secrets_are_required()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Songly:Security:ProductionSecretsRequired"] = "true" })
            .Build();

        var result = new TokenEncryptionKeyValidator(configuration).Validate(null, new NowPlayingOptions { TokenEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });

        Assert.True(result.Succeeded);
    }

    private static TokenEncryptor Encryptor(string key) => new(Options.Create(new NowPlayingOptions { TokenEncryptionKey = key }));
}
