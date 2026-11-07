# ADR 0004: Authentication and authorization on ASP.NET Core

- Status: accepted
- Date: 2026-11-02

## Context

The behaviour is fixed by the original backend (its ADRs 0003, 0006, 0021, 0022, 0041, 0046 and
Claude.md §9): a short-lived JWT access token plus a rotated refresh token stored as a hash;
reuse of a consumed refresh token revokes every session of the account; roles are read from the
database on every request, so suspending an account or revoking an admin role works immediately;
a suspended account gets `ROLE_SUSPENDED` *instead of* `ROLE_USER`; every endpoint requires
`ROLE_USER` unless listed otherwise; the whole `/api/v1/admin/**` prefix requires `ROLE_ADMIN`.

Spring Security expresses most of that in one URL-based filter chain. ASP.NET Core splits it
between authentication handlers, authorization policies and endpoint metadata. This ADR records how
each rule is mapped and where the port deliberately differs.

## Decisions

**Tokens.** Validation uses the standard `JwtBearer` handler; issuing uses `JsonWebTokenHandler`.
HS256, `sub` = account id and nothing else, no issuer/audience (one application issues and accepts
them), **zero clock skew** (the default 5 minutes would silently extend every 15-minute token by a
third). Inbound claim mapping is off, so `sub` stays `sub`.

**Roles from the live account state.** `JwtBearerEvents.OnTokenValidated` loads the account
(one query: admin flag and suspension) and adds role claims to the principal. Nothing about roles
is in the token.

**Closed by default.** The authorization *fallback policy* requires the `USER` role. It applies to
every endpoint without authorization metadata — so a new endpoint is closed, also to suspended
accounts, unless it says `AllowAnonymous()` or names a policy.

**Admin area by path, in middleware.** A guard after authentication answers everything under
`/api/v1/admin` with 401/403 unless the caller is an administrator. A policy attached per endpoint
would protect an admin endpoint only once someone remembers to attach it — the risk the original
avoided with a URL matcher. The guard also ignores `AllowAnonymous()`: the prefix decides.

**Error responses.** An `IAuthorizationMiddlewareResultHandler` writes `UNAUTHENTICATED`,
`FORBIDDEN` and `ACCOUNT_SUSPENDED` in the API's error format instead of the framework's empty
401/403.

**Refresh rotation is atomic** — an improvement over the original. The session is revoked with a
conditional `UPDATE ... WHERE id = ? AND revoked = false`; if no row was touched, a concurrent
request won the race and this one is treated as reuse. In the original, two requests racing with
the same token could both rotate it, handing a thief a second, undetected session chain.

**Reuse revocation survives the failure.** The original needed `noRollbackFor`, because revoking
all sessions happened inside the transaction that the thrown exception rolled back. Here the
revocation runs outside any transaction and is committed before the exception is thrown. Tests
fail if it is ever rolled back (verified by breaking it on purpose).

**Passwords: bcrypt, cost 10** (`BCrypt.Net-Next`), not ASP.NET Identity's PBKDF2 hasher. Hashes
written by Spring Security's `BCryptPasswordEncoder` verify unchanged, so both backends can share a
database.

**Request validation** uses DataAnnotations with attributes named after the original Bean
Validation ones (`NotBlank`, `Email`, `Size`, `MaxUtf8Bytes`), applied by an endpoint filter on the
group every module maps onto — on by default, unlike `@Valid`. Messages are Polish and fixed; in the
original they came from Hibernate Validator in the JVM's locale, so only the `VALIDATION_ERROR` code
was ever part of the contract.

## Known differences

- For an **anonymous** caller, a wrong HTTP method or content type on an existing route is 401,
  not 405/415. Routing answers those with synthetic endpoints that carry none of the route's
  metadata, so the fallback policy applies to them. With a token the answer is 405/415 as in the
  original. Matching the original exactly would mean special-casing framework internals; no
  client depends on it.
- `UserSessionsRevokedEvent` (consumed by push notifications in the original) is not published
  yet. Domain events arrive together with their first consumer.
- Rate limiting of login and the other public paths, and of writes per user, is ADR 0014. The
  metrics exporter comes with the observability work; the counters (`songly.auth.login.attempts`,
  `songly.auth.refresh.attempts`) are already recorded.
