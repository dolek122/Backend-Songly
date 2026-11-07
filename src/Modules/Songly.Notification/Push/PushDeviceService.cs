using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Songly.Notification.Domain;
using Songly.Notification.Http;
using Songly.Notification.Persistence;

namespace Songly.Notification.Push;

/// <summary>The register of push devices (original ADR 0040).</summary>
internal sealed class PushDeviceService(NotificationDbContext db, IOptions<PushDeviceOptions> options, TimeProvider time)
{
    /// <summary>
    /// Idempotent — the app reports its token at every start and whenever FCM replaces it. A token known under
    /// another account moves: it is the same phone with someone else signed in, and the previous owner must not
    /// keep getting their notifications there. The surplus over the limit is trimmed, oldest first.
    /// </summary>
    public async Task RegisterAsync(Guid userId, string token, PushPlatformDto platformDto, CancellationToken cancellationToken)
    {
        var platform = platformDto == PushPlatformDto.Ios ? PushPlatform.Ios : PushPlatform.Android;
        var now = time.GetUtcNow();
        if (await db.PushDevices.SingleOrDefaultAsync(d => d.Token == token, cancellationToken) is { } device)
        {
            device.AssignTo(userId, platform, now);
        }
        else
        {
            db.PushDevices.Add(new PushDevice(token, userId, platform, now));
        }

        await db.SaveChangesAsync(cancellationToken);

        var surplus = await db.PushDevices.Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UpdatedAt).ThenBy(d => d.Token)
            .Skip(options.Value.MaxDevicesPerUser)
            .Select(d => d.Token)
            .ToListAsync(cancellationToken);
        if (surplus.Count > 0)
        {
            await db.PushDevices.Where(d => surplus.Contains(d.Token)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>Someone else's or an unknown token is no effect, not an error: the answer must not tell whose token it is.</summary>
    public Task UnregisterAsync(Guid userId, string token, CancellationToken cancellationToken) =>
        db.PushDevices.Where(d => d.Token == token && d.UserId == userId).ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> TokensOfAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.PushDevices.AsNoTracking().Where(d => d.UserId == userId).Select(d => d.Token).ToListAsync(cancellationToken);

    /// <summary>The provider says the phone is gone — without this every later push would land nowhere.</summary>
    public Task ForgetAsync(string token, CancellationToken cancellationToken) =>
        db.PushDevices.Where(d => d.Token == token).ExecuteDeleteAsync(cancellationToken);

    public Task DeleteAllForAsync(Guid userId, CancellationToken cancellationToken) =>
        db.PushDevices.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
}
