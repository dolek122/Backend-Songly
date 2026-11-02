# ADR 0005: E-mail channel, Redis interval gates, side effects after the save

- Status: accepted
- Date: 2026-11-02

## Context

Registration (original ADR 0009) is the first feature that talks to the outside world: it sends
e-mail, and it must not let anyone use it to flood someone else's inbox. The original backend
solved that with Spring's `JavaMailSender` behind an async executor (its ADR 0041), Redis keys for
"at most once per interval" (fail-open), and `@TransactionalEventListener(AFTER_COMMIT)` to send
mail only once the data is committed. Each of those needs a .NET counterpart.

## Decisions

**E-mail is a channel in Common**, `IMailSender`, with two providers chosen by
`Songly:Mail:Provider`:

- `log` (the default): writes the whole message, token included, to the log. Development only —
  an instance that sends real mail by mistake is worse than one that only logs it by mistake.
- `smtp`: MailKit behind a **bounded `Channel<T>` drained by two background workers**. Sending
  returns at once, so the response time never depends on the mail server (in flows that send mail
  only for existing accounts, it would reveal which addresses have one), and a hung server cannot
  hold request threads. A full queue throws instead of blocking. The request id travels with the
  queued message, so a delivery failure is logged under the request that caused it. `smtp` without
  a host refuses to start.

**"At most once per interval" is `IIntervalGate`**, backed by Redis (`SET key NX PX interval`), so
the interval is shared by every instance. It **fails open**: with Redis unavailable the call passes,
because these gates protect an inbox from flooding — they are not a condition of correctness. The
Redis client gets 1-second timeouts and does not abort when Redis is down at startup; fail-open is
only real if failing is fast.

**Side effects happen after the save, by statement order.** Spring needed an after-commit event
listener because the transaction was declarative and ended after the method returned. Here the
code saves (`SaveChangesAsync` / `CommitAsync`) and only then sends the mail. A link sent before a
failed save would point to a registration that never existed.

**A write rejected by a database constraint is 409 `CONFLICT`**, not 500 (SQLSTATE class 23 and
EF Core's concurrency exception), logged as a warning with the stack trace — the same symptom
comes from a module that forgot to delete its rows on account deletion. Two clicks on one
verification link are exactly this kind of race.

**Domain events are still not introduced.** The original publishes `UserRegisteredEvent` at
verification; its only consumer is the profile module (it creates the profile). The event
arrives together with that module — publishing it to nobody would be code without behaviour.

## Consequences

- Locally nothing extra is needed (the `log` provider), but Redis now runs in `docker compose`.
- The integration tests run against real Redis (Testcontainers) and, for SMTP, against a real
  test mail server (Mailpit) — the queue, MailKit and the configuration are tested end to end.
- Tests read tokens from the messages the application sent, through a recording decorator around
  the configured channel. They never reach for a token in the database (only its hash is there).
