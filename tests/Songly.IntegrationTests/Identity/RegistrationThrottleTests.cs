using System.Net;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// The interval between messages to one address. Ported from the original
/// <c>RegistrationThrottleIntegrationTest</c>.
/// </summary>
/// <remarks>
/// Without it the endpoint would be a mail generator aimed at any address — worse than password
/// reset, which at least needs an existing account. Rate limiting per IP does not catch it: the
/// victim is one, the source addresses can be many.
/// </remarks>
public sealed class RegistrationThrottleTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>
    /// Repeated registrations within the interval get the same 202, but no message. A
    /// distinguishable 429 would tell the asker a registration for this address was just requested.
    /// </summary>
    [Fact]
    public async Task Repeated_registrations_within_the_interval_send_one_message()
    {
        var email = TestAccounts.UniqueEmail("throttle");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await _client.RegisterAsync(email)).StatusCode);
        }

        Assert.Single(factory.Mails.SentTo(email));
    }

    /// <summary>The same interval protects the owner of an existing account from a flood of warnings.</summary>
    [Fact]
    public async Task Repeated_registrations_on_an_existing_account_send_one_warning()
    {
        var owner = await factory.CreateUserAsync("throttle-existing");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await _client.RegisterAsync(owner.Email, "another-password-123")).StatusCode);
        }

        Assert.Single(factory.Mails.SentTo(owner.Email));
    }

    /// <summary>
    /// The warning interval lives in Redis and fails open: with Redis down the warning still goes
    /// out — one message too many is better than one silenced by mistake — and nothing turns
    /// into a 500.
    /// </summary>
    [Fact]
    public async Task With_redis_down_the_warning_still_goes_out()
    {
        // Nothing listens on port 1.
        using var app = factory.WithSettings(("ConnectionStrings:Redis", "localhost:1"));
        var client = app.CreateClient();
        var owner = await factory.CreateUserAsync("throttle-redis-down");

        Assert.Equal(HttpStatusCode.Accepted, (await client.RegisterAsync(owner.Email, "another-password-123")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.RegisterAsync(owner.Email, "another-password-123")).StatusCode);

        Assert.Equal(2, app.MailsOf().SentTo(owner.Email).Count);
    }
}
