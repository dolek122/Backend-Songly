using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;

namespace Songly.IntegrationTests.NowPlaying;

/// <summary>
/// The live stream read the way a client reads it: one long GET, events parsed from the SSE lines.
/// Events wait in a queue, so a test can <b>wait</b> for the push it caused before checking the next —
/// the channel delivers asynchronously, and a push from before would otherwise land in the middle of
/// an assertion and pretend to be its result.
/// </summary>
internal sealed class LiveStream : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();
    private readonly Task _reading;
    private int _comments;

    private LiveStream(HttpResponseMessage response)
    {
        _response = response;
        _reading = ReadAsync();
    }

    public HttpResponseMessage Response => _response;

    public int Comments => Volatile.Read(ref _comments);

    /// <summary>Opens the stream; the server registers it before the headers come back, so pushes from here on reach it.</summary>
    public static async Task<LiveStream> OpenAsync(HttpClient client, LoggedInUser user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/now-playing/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new LiveStream(response);
    }

    /// <summary>The next <c>now-playing</c> event's payload.</summary>
    public async Task<JsonElement> NextAsync(TimeSpan? timeout = null)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        wait.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            return await _events.Reader.ReadAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException("No now-playing event arrived in time");
        }
    }

    /// <summary>True when nothing arrived within the time — for "this change must not reach that stream".</summary>
    public async Task<bool> NothingWithinAsync(TimeSpan time)
    {
        await Task.Delay(time, TestContext.Current.CancellationToken);
        return !_events.Reader.TryPeek(out _);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _response.Dispose();
        try
        {
            await _reading;
        }
        catch (Exception)
        {
            // The connection was cut on purpose.
        }

        _stop.Dispose();
    }

    private async Task ReadAsync()
    {
        await using var body = await _response.Content.ReadAsStreamAsync(_stop.Token);
        using var reader = new StreamReader(body);
        string? eventName = null;
        string? data = null;
        while (await reader.ReadLineAsync(_stop.Token) is { } line)
        {
            if (line.StartsWith(':'))
            {
                Interlocked.Increment(ref _comments);
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line["data: ".Length..];
            }
            else if (line.Length == 0 && data is not null)
            {
                Assert.Equal("now-playing", eventName);
                _events.Writer.TryWrite(JsonDocument.Parse(data).RootElement.Clone());
                eventName = null;
                data = null;
            }
        }
    }
}
