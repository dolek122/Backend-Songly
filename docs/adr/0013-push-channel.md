# ADR 0013: The push channel

- Status: accepted
- Date: 2026-11-07

## Context

The original sends pushes through FCM HTTP v1 (its ADR 0040): every new inbox entry rings the
recipient's phones, with the block filter applied at the moment of sending and moderation decisions
getting through it. It took only Google's auth library for the service account's OAuth token, and
sent the request with Spring's `RestClient`, rather than the Firebase Admin SDK (gRPC and Guava for
one `POST`). Devices are unpinned together with the sessions: a password change, a reset, a stolen
refresh token detected.

## Decisions

**`Songly.Common.Push` mirrors the mail channel.** `IPushSender` with two implementations: `log`
(the default, which logs the type and the start of the token, never the text) and `fcm`. `fcm`
without a usable service account key fails the start, checked by an options validator, like SMTP
without a host.

**Google's auth library, our `HttpClient`.** `Google.Apis.Auth` turns the service account JSON into
an access token and renews it; the send is one `POST` through a named `HttpClient` with a 5-second
timeout. Only `UNREGISTERED` in the answer forgets a token — never a bare 404 or `INVALID_ARGUMENT`,
which a misconfiguration produces too.

**The push follows the inbox write, not an event.** `NotificationService.RecordAsync` calls
`PushNotifier` when — and only when — it created a new row, so the deduplication of the inbox
(original ADR 0035) holds for push without a second copy of it. The original published an internal
event after its own transaction for the same reason; with no transaction to wait for here, a direct
call says the same with less machinery. Like the mail, nothing the push does can fail the request.

**The texts are a `switch` with no default arm.** A new notification type must not compile until
someone decides what its push says. C# reports a missing named value as CS8509 but also asks for an
arm for unnamed values (CS8524); the switch turns off only CS8524, with a comment, so CS8509 stays the
error the switch exists for.

**Unpinning shares the revocation's transaction.** `UserSessionsRevoked` is published inside a
`ModuleTransaction` on a password change and a reset — the new password, the revoked sessions and the
deleted devices commit together. On a detected token theft, which commits before it answers 401, it is
published right after the revocation.

## Consequences

- One `POST` per device, sequentially, inside the request that made the notification. A notification
  has a handful of recipients' devices at most, and the account's devices are capped (10).
- Password change and reset now run in a `ModuleTransaction` instead of identity's own transaction.
