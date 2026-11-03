using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Identity.Domain;
using Songly.Identity.Persistence;
using Songly.Identity.Security;

namespace Songly.Identity.LocalDevelopment;

/// <summary>
/// Two ready accounts for clicking through the app locally, with the mobile app on an emulator: an
/// administrator and a regular user (original <c>LocalDevAccountsSeeder</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only in the <c>Local</c> environment.</b> The password is in the repository, so the same
/// account on any shared environment would be a publicly known key to the administration. Nothing
/// turns it on by itself: <c>appsettings.Local.json</c> is read only when the environment is chosen
/// explicitly, and <see cref="LocalDevAccountsOptionsValidator"/> refuses to start where real secrets
/// are required.
/// </para>
/// <para>
/// The accounts are created the way e-mail verification creates one, and the administrator role is
/// granted directly: the first administrator is by design created outside the API (original
/// ADR 0021) — hence no entry in the moderation log. Nothing is published yet:
/// <c>UserRegistered</c> arrives with the profile module, and both verification and this seeder
/// publish it then, so the local accounts get profiles like any other.
/// </para>
/// <para>
/// Idempotent: an existing account is left untouched, so a restart resets neither passwords nor
/// whatever someone has clicked together. It runs before the server accepts requests (a hosted
/// service starts before Kestrel), after the migrations.
/// </para>
/// </remarks>
internal sealed partial class LocalDevAccountsSeeder(
    IServiceScopeFactory scopes,
    IOptions<LocalDevAccountsOptions> options,
    PasswordHasher passwords,
    TimeProvider time,
    ILogger<LocalDevAccountsSeeder> logger) : IHostedService
{
    public const string AdminEmail = "admin@songly.local";
    public const string UserEmail = "user@songly.local";
    public const string Password = "Songly123!";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await SeedAsync(db, AdminEmail, admin: true, cancellationToken);
        await SeedAsync(db, UserEmail, admin: false, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(IdentityDbContext db, string email, bool admin, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return;
        }

        var user = new User(email, passwords.Hash(Password), time.GetUtcNow());
        if (admin)
        {
            user.GrantAdmin();
        }

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(user.Id, admin);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local environment: created test account {UserId} (admin={IsAdmin})")]
    private partial void LogSeeded(Guid userId, bool isAdmin);
}
