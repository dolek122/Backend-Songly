using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Mail;

namespace Songly.IntegrationTests;

/// <summary>
/// Records every e-mail the application sends, then hands it on to the configured channel.
/// </summary>
/// <remarks>
/// <para>
/// Verification and reset tokens exist in plain form <b>nowhere but in the sent message</b> — the
/// database holds only their hashes. A test that does not read them from the message would have
/// to reach into the implementation, or not test the path the user actually takes.
/// </para>
/// <para>
/// A decorator, not a replacement: the configured channel still runs behind it, so a test that
/// switches to SMTP gets real delivery and the recording at the same time.
/// </para>
/// </remarks>
public sealed partial class RecordingMailSender(IMailSender inner) : IMailSender
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<OutgoingEmail>> _sent = new(StringComparer.Ordinal);

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        _sent.GetOrAdd(email.To, _ => new ConcurrentQueue<OutgoingEmail>()).Enqueue(email);
        return inner.SendAsync(email, cancellationToken);
    }

    public IReadOnlyList<OutgoingEmail> SentTo(string recipient) =>
        _sent.TryGetValue(recipient, out var mails) ? mails.ToList() : [];

    /// <summary>
    /// Forgets what was sent to <paramref name="recipient"/> — for a helper whose own mail (the
    /// verification link of a new account) is setup, not what the test is about.
    /// </summary>
    public void ClearFor(string recipient) => _sent.TryRemove(recipient, out _);

    public OutgoingEmail LastTo(string recipient) =>
        SentTo(recipient).LastOrDefault() ?? throw new InvalidOperationException($"No e-mail was sent to {recipient}");

    /// <summary>The token from the link in the last message to <paramref name="recipient"/>.</summary>
    public string TokenSentTo(string recipient)
    {
        var match = TokenInLink().Match(LastTo(recipient).Body);
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException($"The last e-mail to {recipient} has no link with a token");
    }

    /// <summary>Wraps whatever the application registered as its mail channel.</summary>
    public static void Install(IServiceCollection services)
    {
        var channel = services.Last(descriptor => descriptor.ServiceType == typeof(IMailSender));
        services.Remove(channel);
        services.AddSingleton(provider => new RecordingMailSender(Create(provider, channel)));
        services.AddSingleton<IMailSender>(provider => provider.GetRequiredService<RecordingMailSender>());
    }

    private static IMailSender Create(IServiceProvider provider, ServiceDescriptor channel) =>
        (IMailSender)(channel.ImplementationInstance
            ?? channel.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, channel.ImplementationType!));

    [GeneratedRegex("token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenInLink();
}

public static class RecordingMailSenderExtensions
{
    public static RecordingMailSender MailsOf(this WebApplicationFactory<Program> app) =>
        app.Services.GetRequiredService<RecordingMailSender>();
}
