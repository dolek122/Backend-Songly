using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Songly.Common.Web;

namespace Songly.NowPlaying.Stream;

/// <summary>One open stream: the frames waiting to be written to its response.</summary>
/// <remarks>
/// Bounded: a client that stops reading must not make the server keep every frame it never takes. A
/// full queue means a dead or hopelessly slow client, and the stream is closed — it reconnects and gets
/// the current state from <c>GET /friends</c>.
/// </remarks>
internal sealed class StreamConnection(IReadOnlySet<Guid> friendsAtOpen)
{
    private readonly Channel<string> _frames = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { SingleReader = true });

    public ChannelReader<string> Frames => _frames.Reader;

    /// <summary>The viewer's friends when the stream opened — whose entries the client may be showing.</summary>
    public IReadOnlySet<Guid> FriendsAtOpen { get; } = friendsAtOpen;

    public bool TryWrite(string frame) => _frames.Writer.TryWrite(frame);

    public void Close() => _frames.Writer.TryComplete();
}

internal sealed class TooManyStreamsException(int maxPerUser)
    : ApiException(StatusCodes.Status429TooManyRequests, "TOO_MANY_STREAMS", $"Przekroczono limit otwartych strumieni presence ({maxPerUser})");

/// <summary>
/// The open live streams of this instance, per user (original ADR 0007, stage 1). In memory by nature:
/// a stream is an open socket of this process. The fan-out between instances is Redis's job
/// (<see cref="StatusPublisher"/>); after a restart the clients reconnect by themselves, so nothing here
/// needs to last.
/// </summary>
/// <remarks>
/// Long-living connections are a resource to watch (stage 4): the streams per user are limited, dead
/// ones are found by the heartbeat, and the registry is visible in metrics —
/// <c>songly.nowplaying.streams.active</c> and <c>songly.nowplaying.stream.events{outcome}</c>.
/// </remarks>
internal sealed class StreamHub
{
    public const string MeterName = "Songly.NowPlaying.Stream";

    private readonly Dictionary<Guid, HashSet<StreamConnection>> _streams = [];
    private readonly Lock _gate = new();
    private readonly IOptions<StreamOptions> _options;
    private readonly Counter<long> _events;

    public StreamHub(IOptions<StreamOptions> options, IMeterFactory meterFactory)
    {
        _options = options;
        var meter = meterFactory.Create(MeterName);
        meter.CreateObservableGauge("songly.nowplaying.streams.active", OpenStreamCount, description: "Open presence streams on this instance");
        _events = meter.CreateCounter<long>("songly.nowplaying.stream.events");
    }

    /// <exception cref="TooManyStreamsException">The user has the most streams allowed already; the earlier ones stay untouched.</exception>
    public StreamConnection Register(Guid userId, IReadOnlySet<Guid> friendsAtOpen)
    {
        var maxPerUser = _options.Value.MaxPerUser;
        lock (_gate)
        {
            if (!_streams.TryGetValue(userId, out var connections))
            {
                connections = [];
                _streams[userId] = connections;
            }

            if (connections.Count >= maxPerUser)
            {
                throw new TooManyStreamsException(maxPerUser);
            }

            var connection = new StreamConnection(friendsAtOpen);
            connections.Add(connection);
            return connection;
        }
    }

    public void Unregister(Guid userId, StreamConnection connection)
    {
        connection.Close();
        lock (_gate)
        {
            if (_streams.TryGetValue(userId, out var connections) && connections.Remove(connection) && connections.Count == 0)
            {
                _streams.Remove(userId);
            }
        }
    }

    /// <summary>An event to every open stream of the user; nothing when there is none.</summary>
    public void Push(Guid userId, string frame)
    {
        foreach (var connection in ConnectionsOf(userId))
        {
            if (connection.TryWrite(frame))
            {
                _events.Add(1, new KeyValuePair<string, object?>("outcome", "delivered"));
            }
            else
            {
                // A client that does not read: closed, it reconnects by itself.
                _events.Add(1, new KeyValuePair<string, object?>("outcome", "dropped"));
                Unregister(userId, connection);
            }
        }
    }

    /// <summary>An SSE comment, which clients ignore — keeps the connection alive through proxies.</summary>
    public void Heartbeat()
    {
        foreach (var userId in ConnectedUserIds())
        {
            Push(userId, ": keep-alive\n\n");
        }
    }

    /// <summary>Whether the user has at least one open stream — someone watching.</summary>
    public bool HasSubscribers(Guid userId)
    {
        lock (_gate)
        {
            return _streams.ContainsKey(userId);
        }
    }

    /// <summary>Lets a message from the channel be dropped without asking the friend graph, on an instance holding no stream.</summary>
    public bool HasAnySubscriber()
    {
        lock (_gate)
        {
            return _streams.Count > 0;
        }
    }

    /// <summary>
    /// The viewers whose stream opened while <paramref name="subjectId"/> was their friend — who may be
    /// showing the subject even if the friendship has gone since (with a deleted account, for one).
    /// </summary>
    public IReadOnlyList<Guid> ViewersWhoKnew(Guid subjectId)
    {
        lock (_gate)
        {
            return [.. _streams.Where(entry => entry.Value.Any(connection => connection.FriendsAtOpen.Contains(subjectId))).Select(entry => entry.Key)];
        }
    }

    /// <summary>The users with at least one open stream — the viewers whose friends the poll observes.</summary>
    public IReadOnlyList<Guid> ConnectedUserIds()
    {
        lock (_gate)
        {
            return [.. _streams.Keys];
        }
    }

    private int OpenStreamCount()
    {
        lock (_gate)
        {
            return _streams.Values.Sum(connections => connections.Count);
        }
    }

    private List<StreamConnection> ConnectionsOf(Guid userId)
    {
        lock (_gate)
        {
            return _streams.TryGetValue(userId, out var connections) ? [.. connections] : [];
        }
    }
}
