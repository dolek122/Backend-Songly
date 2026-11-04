# ADR 0011: The live now-playing stream

- Status: accepted
- Date: 2026-11-04

## Context

The original pushes the friends' statuses over Server-Sent Events (its ADR 0007): one long `GET`,
an event `now-playing` whenever a friend's status changes, and a comment line every 20 seconds to
keep proxies from closing a silent connection. Changes reach every instance through a Redis
channel; each instance pushes them to its own open streams. Spring gives that an `SseEmitter`.
ASP.NET Core 10 has a `ServerSentEvents` result, but it writes events only — no comment lines.

## Decisions

**The stream is written by hand.** The endpoint sets `text/event-stream`, then copies frames from the
connection's queue to the response, flushing after each. It is a dozen lines, and it can send the
`: keep-alive` comment the mobile app already ignores; an event instead would be a new thing for
the client to understand. The connection is registered before the response starts, so the limit of
streams per user is an ordinary `429 TOO_MANY_STREAMS` with the usual JSON body.

**Each stream has a bounded queue** (`Channel<string>`, 64 frames). Pushing never waits for a socket:
the broadcaster writes to the queue, and the endpoint's own loop writes to the network. A client that
stops reading fills its queue; the stream is then closed rather than buffered without limit, and the
client reconnects and fetches the current state from `GET /friends`.

**Every change goes through the channel**, also when the stream is on the publishing instance. One
delivery path to test. The channel's message (`StatusChanged`) is a contract between instances: during
a rolling deploy old and new ones read it, so fields may be added, never removed or changed in meaning.

**"Stopped listening" also goes to the friends a stream had when it opened.** This departs from the
original. A deleted account publishes "stopped" after its commit, when its friendships are already
gone; counting friends at that moment finds nobody, so the original's friends kept seeing the deleted
account as playing until they reconnected (its unit test mocked the friend graph and could not see
it). Each connection keeps the viewer's friend ids from when it opened, and a "stopped" message goes to
the current friends and to those viewers. A status still goes to current friends only; "stopped"
carries nothing but the user's id, which those viewers already have on screen.

## Consequences

- Opening a stream costs one query to the friend graph.
- Tests read the stream as a client does (`LiveStream`), and publish to the Redis channel directly to
  test what another instance would send.
