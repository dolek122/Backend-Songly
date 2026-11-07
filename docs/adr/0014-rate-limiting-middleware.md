# ADR 0014: Rate limiting as two middlewares

- Status: accepted
- Date: 2026-11-07

## Context

The original limits requests in two Spring Security filters (its Claude.md §9 and ADR 0034):

- **Per client address on the public paths** (`RateLimitingFilter`), before authentication.
- **Per user on writes behind a token** (`UserRateLimitingFilter`), after the JWT filter. Its rules are named
  groups of paths with one counter each.

Both count a fixed window in Redis, let the request through when Redis is down, answer 429
`RATE_LIMIT_EXCEEDED` with `Retry-After`, and give a window that lost its expiry its expiry back. Behind
Render's proxy the real address comes from `server.forward-headers-strategy=framework`.

ASP.NET Core has a rate limiter of its own (`Microsoft.AspNetCore.RateLimiting`). Its windows are in-process,
per instance.

## Decisions

**Our own middleware over Redis, not ASP.NET Core's limiter.**
- The built-in limiter's windows live in one process. Two instances would give every client twice the budget,
  and a restart would hand every client a fresh window. The original's windows are shared through Redis
  deliberately.
- Its partitioned policies could key by user, but not by the original's named groups of paths. They would also
  answer in a format other than the API's own.
- Two middlewares in `Songly.Common.RateLimiting`, over a shared `FixedWindowCounter`, are a direct port of
  the two filters. They know nothing of the domain: the user middleware reads only the token's subject.

**The address limit runs before authentication; the user limit runs after authorization.**
- The original placed its user filter before authorization. Here, the identity module's pipeline ends with
  `UseAuthorization`, so the user limiter is placed after the modules' pipelines.
- The only difference: a request that authorization refuses anyway (a suspended account's write, an anonymous
  write) is not counted. Such a request never reaches a handler, so it reaches no one's inbox either, and the
  limit exists to protect inboxes.

**Path patterns in the original's syntax.**
- `*` is one segment and `**` is any number of segments. `PathPattern` is a few lines with unit tests.
- A window is keyed by the pattern, not the path. Otherwise a wildcard would give every id its own budget.

**Defaults are filled in only when nothing is configured.**
- The configuration binder appends to a list it finds already filled. Default paths, rules or methods set as
  property initialisers would therefore be extended by a configured list, not replaced by it — the original
  replaces.
- So the lists start empty, and the middlewares fall back to the defaults when they are empty.

**Forwarded headers trust exactly one hop.**
- `Songly:ForwardedHeaders:Enabled` turns on `UseForwardedHeaders` where the application runs behind a proxy.
- The proxy's own address is not known in advance on a PaaS, so the list of trusted proxies is cleared.
- `ForwardLimit = 1` then takes the *last* address in `X-Forwarded-For`, the one the proxy appended itself.
  Anything the client wrote in front of it is ignored, so a client cannot buy a fresh window with a made-up
  address.
- Off by default: without a proxy in front, the header is whatever the client sends.

## Consequences

- **The shared test instance has both limits off**, as the original's base test class does. Flow tests make
  series of writes with one account and logins from one address. The limits' own tests run in instances of
  their own:
  - with small limits;
  - behind the forwarded-headers middleware;
  - with one made-up address per test, because the windows live in the Redis every test shares.
- **Every write can now answer 429.** The mobile app handles that globally from `Retry-After`, as it does
  against the original.
- **Fixed windows have the known edge**: up to twice the limit across a window's boundary. That is acceptable
  against a script, as in the original.
