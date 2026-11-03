# ADR 0008: The new-account event runs in the account's transaction

- Status: accepted
- Date: 2026-11-03

## Context

A new account needs a profile — and, as modules arrive, more: analytics counts registrations,
notification settings start with defaults. The original publishes `UserRegisteredEvent` when the
address is confirmed and handles it with `@TransactionalEventListener(AFTER_COMMIT)`: the profile
is created after the account is committed, in a transaction of its own.

The cost is invisible until something fails. Spring logs an exception from an after-commit
listener and carries on, so a profile that failed to be created left an **account without a
profile**: the user is logged in, `/profiles/me` answers 404 forever, and nobody can find them in
the search. Nothing repairs it, because the event never comes again.

## Decision

**`UserRegistered` is published inside the transaction that creates the account**, after the
`users` row is saved and before the session is opened. Verification runs in a
`ModuleTransaction` (ADR 0006), so every handler — the profile module's first — is part of it:
the account, its profile and its session exist together or not at all.

A failing handler fails the confirmation with a 500. The pending registration is rolled back with
everything else, so **the same link confirms the address** once the cause is gone. The test
shows both: a probe handler fails, there is no account, the link works the second time.

The shared transaction is not only for the failure case. The profile row references `users`; a
handler writing through its own, separate transaction would not see the uncommitted account and
the insert would fail on the foreign key.

The local test accounts (ADR 0007) are created the same way, so they have profiles too.

## Consequences

- Handlers of `UserRegistered` must be quick and must not talk to the outside world: they hold
  the verification's transaction open, and a side effect (mail, push) sent from inside it would
  stay sent if the transaction rolled back. A handler that needs to react to a committed account
  publishes its own after-the-save step, as ADR 0006 describes.
- The event carries no e-mail address (original ADR 0046) — see `UserRegistered`.
- Test accounts are now created by registration (`factory.CreateUserAsync()`), not by SQL: an
  account without its profile is a state production can no longer produce, and tests should not
  run on it. The integration suite takes about twice as long (bcrypt at the application's cost on
  every registration and login), roughly 40 seconds.
