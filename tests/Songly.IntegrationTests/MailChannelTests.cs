using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;

namespace Songly.IntegrationTests;

/// <summary>The SMTP channel against a real (test) mail server, and its startup check.</summary>
public sealed class MailChannelTests(SonglyApiFactory factory)
{
    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    /// <summary>
    /// Registration through the queued SMTP channel delivers a real message with the link — the
    /// request returns before delivery, so the test waits for the message to arrive.
    /// </summary>
    [Fact]
    public async Task Smtp_delivers_the_verification_link_from_the_queue()
    {
        await using var mailpit = new ContainerBuilder("axllent/mailpit:v1.33.0")
            .WithPortBinding(SmtpPort, assignRandomHostPort: true)
            .WithPortBinding(ApiPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiPort).ForPath("/api/v1/messages")))
            .Build();
        await mailpit.StartAsync(TestContext.Current.CancellationToken);

        using var app = factory.WithSettings(
            ("Songly:Mail:Provider", "smtp"),
            ("Songly:Mail:Smtp:Host", mailpit.Hostname),
            ("Songly:Mail:Smtp:Port", mailpit.GetMappedPublicPort(SmtpPort).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var email = TestAccounts.UniqueEmail("smtp");

        Assert.Equal(HttpStatusCode.Accepted, (await app.CreateClient().RegisterAsync(email)).StatusCode);

        using var mailbox = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(ApiPort)}") };
        var message = await WaitForMessageTo(mailbox, email);
        Assert.Equal("Songly — potwierdź swój adres e-mail", message.GetProperty("Subject").GetString());
        Assert.Equal("no-reply@songly.io", message.GetProperty("From").GetProperty("Address").GetString());
        Assert.Contains($"token={app.MailsOf().TokenSentTo(email)}", message.GetProperty("Text").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// SMTP without a host must not start: an instance that only looks as if it sent mail is an
    /// outage nobody notices until the first "I never got the link". Only the failure is asserted
    /// — the message is racy through <c>WebApplicationFactory</c> (see <c>AccessTokenTests</c>) and
    /// asserted in <c>MailOptionsValidatorTests</c> instead. The SMTP test above, with a host,
    /// shows that the host is what decides.
    /// </summary>
    [Fact]
    public void Smtp_without_a_host_refuses_to_start()
    {
        using var app = factory.WithSettings(("Songly:Mail:Provider", "smtp"), ("Songly:Mail:Smtp:Host", ""));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    [Fact]
    public void An_unknown_provider_refuses_to_start()
    {
        using var app = factory.WithSettings(("Songly:Mail:Provider", "carrier-pigeon"));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    private static async Task<JsonElement> WaitForMessageTo(HttpClient mailbox, string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var search = await mailbox.GetFromJsonAsync<JsonElement>($"/api/v1/search?query=to:{Uri.EscapeDataString(email)}", TestContext.Current.CancellationToken);
            var found = search.GetProperty("messages").EnumerateArray().FirstOrDefault();
            if (found.ValueKind == JsonValueKind.Object)
            {
                return await mailbox.GetFromJsonAsync<JsonElement>($"/api/v1/message/{found.GetProperty("ID").GetString()}", TestContext.Current.CancellationToken);
            }

            Assert.True(DateTime.UtcNow < deadline, $"No message to {email} arrived within 15 seconds");
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }
}
