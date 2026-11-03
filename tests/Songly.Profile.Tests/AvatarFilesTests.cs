using Microsoft.Extensions.Logging.Abstractions;
using Songly.Common.Storage;
using Songly.Profile.Profiles;

namespace Songly.Profile.Tests;

/// <summary>Ported from the original <c>OrphanedAvatarCleanerTest</c>.</summary>
public sealed class AvatarFilesTests
{
    /// <summary>
    /// The file is deleted once nothing points to it any more — after the save or the commit — so
    /// there is nothing left to undo. A storage failure must not turn a successful change of avatar,
    /// or a completed account deletion, into an error: the file stays orphaned, and the log says so.
    /// </summary>
    [Fact]
    public async Task A_storage_failure_is_logged_instead_of_thrown()
    {
        var avatars = new AvatarFiles(new FailingStorage(), NullLogger<AvatarFiles>.Instance);

        await avatars.DeleteOrphanedAsync("avatars/x/y.png", TestContext.Current.CancellationToken);
    }

    private sealed class FailingStorage : IObjectStorage
    {
        public Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
            throw new ObjectStorageException("Nie udało się skasować pliku", new IOException("storage down"));

        public string PublicUrl(string key) => key;
    }
}
