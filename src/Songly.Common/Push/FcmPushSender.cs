using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Songly.Common.Push;

/// <summary>
/// Sending through FCM HTTP v1 (original ADR 0040). One request per device — v1 has no batch send, and a
/// notification has a few recipients' devices at most.
/// </summary>
/// <remarks>
/// Android gets a data-only message, so the app builds the notification itself (channel, action on tap),
/// in the background too. iOS would show nothing without an <c>alert</c> while the app sleeps — hence the
/// separate <c>apns</c> block, which does not affect Android.
/// </remarks>
internal sealed class FcmPushSender(HttpClient http, string projectId, Func<CancellationToken, Task<string>> accessToken) : IPushSender
{
    public const string HttpClientName = "Fcm";

    public async Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        var body = new
        {
            message = new
            {
                token = message.Token,
                data = message.Data,
                android = new { priority = "high" },
                apns = new { payload = new { aps = new { alert = new { title = message.Title, body = message.Body } } } },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/projects/{projectId}/messages:send") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken(cancellationToken));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PushDeliveryException("FCM did not answer", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw Failure((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            }
        }
    }

    /// <summary>
    /// <c>UNREGISTERED</c> is the only error that surely speaks of the token itself. <c>INVALID_ARGUMENT</c> can
    /// be a bad token or a bad request — forgetting tokens on it could quietly unpin every device after one bug
    /// in the code — so it is an ordinary failure.
    /// </summary>
    /// <remarks>
    /// For the same reason <b>a 404 alone is not enough</b> (original ADR 0049): a wrong FCM address or project
    /// id in the configuration gives a 404 too, and then every push would forget its recipient's phone. FCM
    /// always adds <c>UNREGISTERED</c> for a stale token.
    /// </remarks>
    private static Exception Failure(int status, string body) =>
        body.Contains("UNREGISTERED", StringComparison.Ordinal)
            ? new InvalidPushTokenException("FCM does not know this device")
            : new PushDeliveryException($"FCM refused the message: HTTP {status}");
}
