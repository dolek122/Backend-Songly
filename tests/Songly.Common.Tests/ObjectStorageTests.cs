using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Storage;

namespace Songly.Common.Tests;

/// <summary>
/// A storage failure reaches the caller as <see cref="ObjectStorageException"/> (502), not as a
/// 500 with the SDK's stack trace — and a failed delete is an error, not swallowed (original
/// ADR 0049). The real storage is tested against MinIO in the integration tests.
/// </summary>
public sealed class ObjectStorageTests
{
    // Nothing listens on port 1: every call fails at once.
    private readonly IObjectStorage _unreachable = new ServiceCollection()
        .AddSonglyStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Songly:Storage:Endpoint"] = "http://127.0.0.1:1",
            ["Songly:Storage:PublicBaseUrl"] = "http://cdn.example/songly-media",
            ["Songly:Storage:Timeout"] = "00:00:02",
        }).Build())
        .BuildServiceProvider()
        .GetRequiredService<IObjectStorage>();

    [Fact]
    public async Task A_failed_delete_is_reported_to_the_caller()
    {
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => _unreachable.DeleteAsync("avatars/x.png", TestContext.Current.CancellationToken));

        Assert.Equal("STORAGE_ERROR", error.Code);
        Assert.Equal(502, error.StatusCode);
    }

    [Fact]
    public async Task A_failed_upload_is_reported_to_the_caller() =>
        await Assert.ThrowsAsync<ObjectStorageException>(() => _unreachable.PutAsync("avatars/x.png", [1, 2, 3], "image/png", TestContext.Current.CancellationToken));

    /// <summary>Clients load files from the CDN's address, not from the API's.</summary>
    [Fact]
    public void Public_urls_come_from_the_public_base_url() =>
        Assert.Equal("http://cdn.example/songly-media/avatars/x.png", _unreachable.PublicUrl("avatars/x.png"));
}
