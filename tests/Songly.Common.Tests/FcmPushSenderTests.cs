using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Songly.Common.Push;

namespace Songly.Common.Tests;

/// <summary>
/// The FCM request and the reading of its errors. Ported from the original <c>FcmPushSenderTest</c>, which ran
/// against a local HTTP server; here the server is the client's handler.
/// </summary>
public sealed class FcmPushSenderTests
{
    private readonly FakeFcm _fcm = new();

    /// <summary>Data only for Android (the app builds the notification), an alert for iOS (nothing shows otherwise while the app sleeps).</summary>
    [Fact]
    public async Task Sends_data_for_android_and_an_alert_for_ios()
    {
        await Sender().SendAsync(Message(), TestContext.Current.CancellationToken);

        Assert.Equal("Bearer access-token", _fcm.Authorization);
        Assert.Equal("/v1/projects/songly-test/messages:send", _fcm.Path);
        var message = JsonDocument.Parse(_fcm.Body!).RootElement.GetProperty("message");
        Assert.Equal("device-token", message.GetProperty("token").GetString());
        Assert.Equal("POST_LIKED", message.GetProperty("data").GetProperty("type").GetString());
        Assert.Equal("high", message.GetProperty("android").GetProperty("priority").GetString());
        var alert = message.GetProperty("apns").GetProperty("payload").GetProperty("aps").GetProperty("alert");
        Assert.Equal("Ada", alert.GetProperty("title").GetString());
        Assert.Equal("Polubienie Twojego posta", alert.GetProperty("body").GetString());
        Assert.False(message.TryGetProperty("notification", out _));
    }

    [Fact]
    public async Task An_unregistered_token_is_reported_as_invalid()
    {
        _fcm.Respond(HttpStatusCode.NotFound, """{"error":{"status":"NOT_FOUND","details":[{"errorCode":"UNREGISTERED"}]}}""");

        await Assert.ThrowsAsync<InvalidPushTokenException>(() => Sender().SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unregistered_under_another_status_is_still_invalid()
    {
        _fcm.Respond(HttpStatusCode.BadRequest, """{"error":{"details":[{"errorCode":"UNREGISTERED"}]}}""");

        await Assert.ThrowsAsync<InvalidPushTokenException>(() => Sender().SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A 404 alone is not enough (original ADR 0049): a wrong FCM address or project id gives a 404 too, and then
    /// every push would forget its recipient's phone.
    /// </summary>
    [Fact]
    public async Task A_not_found_without_unregistered_is_an_ordinary_failure()
    {
        _fcm.Respond(HttpStatusCode.NotFound, "Not Found");

        await Assert.ThrowsAsync<PushDeliveryException>(() => Sender().SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    /// <summary><c>INVALID_ARGUMENT</c> can be a bad request as well as a bad token — forgetting tokens on it could unpin every phone after one bug.</summary>
    [Fact]
    public async Task Invalid_argument_is_an_ordinary_failure()
    {
        _fcm.Respond(HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT"}}""");

        await Assert.ThrowsAsync<PushDeliveryException>(() => Sender().SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_server_error_is_an_ordinary_failure()
    {
        _fcm.Respond(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<PushDeliveryException>(() => Sender().SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task No_answer_is_an_ordinary_failure()
    {
        _fcm.Fail(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<PushDeliveryException>(() => Sender().SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    /// <summary>The mail channel's rule: <c>fcm</c> without a usable key fails the start, rather than an instance that only looks like it sends.</summary>
    [Fact]
    public void Fcm_without_a_usable_key_does_not_start()
    {
        var validator = new PushOptionsValidator();

        Assert.True(validator.Validate(null, new PushOptions()).Succeeded);
        Assert.True(validator.Validate(null, new PushOptions { Provider = "fcm" }).Failed);
        Assert.True(validator.Validate(null, new PushOptions { Provider = "fcm", FcmCredentialsJson = "{\"type\":\"service_account\"}" }).Failed);
        Assert.True(validator.Validate(null, new PushOptions { Provider = "pigeon" }).Failed);
        Assert.True(validator.Validate(null, new PushOptions { Provider = "fcm", FcmCredentialsJson = ServiceAccountJson() }).Succeeded);
    }

    private FcmPushSender Sender() =>
        new(new HttpClient(_fcm) { BaseAddress = new Uri("https://fcm.test/") }, "songly-test", _ => Task.FromResult("access-token"));

    private static PushMessage Message() =>
        new("device-token", "Ada", "Polubienie Twojego posta", new Dictionary<string, string> { ["type"] = "POST_LIKED" });

    private static string ServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "songly-test",
            ["private_key_id"] = "key-id",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "push@songly-test.iam.gserviceaccount.com",
            ["client_id"] = "1",
            ["token_uri"] = "https://oauth2.googleapis.com/token",
        });
    }

    private sealed class FakeFcm : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = """{"name":"projects/songly-test/messages/1"}""";
        private Exception? _failure;

        public string? Authorization { get; private set; }

        public string? Path { get; private set; }

        public string? Body { get; private set; }

        public void Respond(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public void Fail(Exception failure) => _failure = failure;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _failure is not null
                ? throw _failure
                : new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
