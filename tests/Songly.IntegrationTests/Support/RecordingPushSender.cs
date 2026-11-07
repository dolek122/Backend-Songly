using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Push;

namespace Songly.IntegrationTests;

/// <summary>
/// Records every push by device token, in front of the application's own push channel — like the mail
/// recorder. A token marked unregistered answers the way FCM does for a phone that is gone.
/// </summary>
public sealed class RecordingPushSender(IPushSender inner) : IPushSender
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PushMessage>> _sent = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _unregistered = new(StringComparer.Ordinal);

    public Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        if (_unregistered.ContainsKey(message.Token))
        {
            throw new InvalidPushTokenException("FCM does not know this device");
        }

        _sent.GetOrAdd(message.Token, _ => new ConcurrentQueue<PushMessage>()).Enqueue(message);
        return inner.SendAsync(message, cancellationToken);
    }

    public IReadOnlyList<PushMessage> SentTo(string token) =>
        _sent.TryGetValue(token, out var pushes) ? pushes.ToList() : [];

    public void MarkUnregistered(string token) => _unregistered[token] = true;

    /// <summary>Wraps whatever the application registered as its push channel.</summary>
    public static void Install(IServiceCollection services)
    {
        var channel = services.Last(descriptor => descriptor.ServiceType == typeof(IPushSender));
        services.Remove(channel);
        services.AddSingleton(provider => new RecordingPushSender(
            (IPushSender)(channel.ImplementationInstance
                ?? channel.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, channel.ImplementationType!))));
        services.AddSingleton<IPushSender>(provider => provider.GetRequiredService<RecordingPushSender>());
    }
}

public static class RecordingPushSenderExtensions
{
    public static RecordingPushSender PushesOf(this WebApplicationFactory<Program> app) =>
        app.Services.GetRequiredService<RecordingPushSender>();
}
