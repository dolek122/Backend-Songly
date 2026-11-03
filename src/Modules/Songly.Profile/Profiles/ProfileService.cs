using Microsoft.EntityFrameworkCore;
using Songly.Common.Media;
using Songly.Common.Paging;
using Songly.Identity.Contracts;
using Songly.Profile.Domain;
using Songly.Profile.Http;
using Songly.Profile.Persistence;

namespace Songly.Profile.Profiles;

/// <summary>The caller's own profile, other users' public profiles, and favourite genres.</summary>
internal sealed class ProfileService(ProfileDbContext db, ISuspendedAccounts suspendedAccounts, AvatarFiles avatars, TimeProvider time)
{
    /// <summary>
    /// The default profile of a new account, created inside the transaction that creates the
    /// account (see <see cref="UserRegistered"/>). Idempotent, so a replayed event does nothing.
    /// </summary>
    public async Task CreateDefaultAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await db.Profiles.AnyAsync(profile => profile.UserId == userId, cancellationToken))
        {
            return;
        }

        db.Profiles.Add(new UserProfile(userId, DefaultDisplayName.Generate(), time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProfileResponse> GetMineAsync(Guid userId, CancellationToken cancellationToken) =>
        ToResponse(await FindAsync(userId, cancellationToken));

    public async Task<ProfileResponse> UpdateMineAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await FindAsync(userId, cancellationToken);
        profile.Update(request.DisplayName!, request.Bio, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    /// <summary>
    /// Anyone's profile, for viewing it in the app, with their favourite genres.
    /// </summary>
    /// <remarks>
    /// The profile of a <b>suspended</b> account is reported as nonexistent (original ADR 0026).
    /// Hiding it from the search alone would be half a measure — a saved link would still work.
    /// 404, not 403: an account's status is nobody else's business, the same rule as for posts of
    /// blocked authors. Hidden, never deleted: when the suspension is lifted or expires, the
    /// profile is back as it was.
    /// </remarks>
    public async Task<PublicProfileResponse> GetPublicAsync(Guid userId, CancellationToken cancellationToken)
    {
        if ((await suspendedAccounts.SuspendedUserIdsAsync(cancellationToken)).Contains(userId))
        {
            throw new ProfileNotFoundException();
        }

        var profile = await FindAsync(userId, cancellationToken);
        return new PublicProfileResponse(
            profile.UserId,
            profile.DisplayName,
            profile.Bio,
            avatars.UrlOf(profile),
            await FavoriteGenresAsync(userId, cancellationToken));
    }

    /// <summary>
    /// The fixed genre list, in the order of the schema's seed — which is what the original's
    /// unordered query returned in practice.
    /// </summary>
    public Task<PagedResponse<GenreResponse>> ListGenresAsync(PageQuery page, CancellationToken cancellationToken) =>
        db.Genres.AsNoTracking().OrderBy(genre => genre.Id).ToPageAsync(page, ToResponse, cancellationToken);

    /// <summary>Replaces the whole set. Every id must be a known genre, or nothing changes.</summary>
    public async Task SetFavoriteGenresAsync(Guid userId, IReadOnlySet<Guid> genreIds, CancellationToken cancellationToken)
    {
        if (await db.Genres.CountAsync(genre => genreIds.Contains(genre.Id), cancellationToken) != genreIds.Count)
        {
            throw new UnknownGenreException();
        }

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.UserGenres.Where(g => g.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        db.UserGenres.AddRange(genreIds.Select(genreId => new UserGenre(userId, genreId, now)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GenreResponse>> FavoriteGenresAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserGenres
            .Where(userGenre => userGenre.UserId == userId)
            .Join(db.Genres, userGenre => userGenre.GenreId, genre => genre.Id, (_, genre) => genre)
            .OrderBy(genre => genre.Id)
            .Select(genre => new GenreResponse(genre.Id, genre.Name))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Stores a new avatar and returns the updated profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file goes <b>through the backend</b>, not straight to the storage on a pre-signed URL
    /// (original ADR 0010): only here can the bytes be checked to be an image and stripped of their
    /// metadata (<see cref="SanitizedImage"/>). With a pre-signed URL the client uploads anything
    /// and gets a public address on our domain for it.
    /// </para>
    /// <para>
    /// The file is stored <b>before</b> the save, so a failed save leaves it orphaned. That is the
    /// side of the mistake chosen on purpose: the other order could leave a row pointing to a file
    /// that is not there. The previous file is deleted <b>after</b> the save, for the same reason.
    /// </para>
    /// </remarks>
    public async Task<ProfileResponse> UploadAvatarAsync(Guid userId, byte[] content, CancellationToken cancellationToken)
    {
        var image = SanitizedImage.From(content);
        var profile = await FindAsync(userId, cancellationToken);

        var key = AvatarFiles.NewKey(userId, image.Type);
        await avatars.StoreAsync(key, image, cancellationToken);
        var previous = profile.ChangeAvatar(key, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        await DeleteIfOrphanedAsync(previous, cancellationToken);
        return ToResponse(profile);
    }

    public async Task DeleteAvatarAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await FindAsync(userId, cancellationToken);
        var previous = profile.ChangeAvatar(null, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        await DeleteIfOrphanedAsync(previous, cancellationToken);
    }

    private Task DeleteIfOrphanedAsync(string? key, CancellationToken cancellationToken) =>
        key is null ? Task.CompletedTask : avatars.DeleteOrphanedAsync(key, cancellationToken);

    private async Task<UserProfile> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Profiles.SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken)
            ?? throw new ProfileNotFoundException();

    private ProfileResponse ToResponse(UserProfile profile) =>
        new(profile.UserId, profile.DisplayName, profile.Bio, avatars.UrlOf(profile));

    private static GenreResponse ToResponse(Genre genre) => new(genre.Id, genre.Name);
}
