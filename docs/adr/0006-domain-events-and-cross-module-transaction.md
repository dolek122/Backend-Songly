# ADR 0006: Domain events and one transaction across modules

- Status: accepted
- Date: 2026-11-02

## Context

Account deletion (original Claude.md §9) is the first operation that spans modules. No table has a
foreign key to `users` with `ON DELETE CASCADE` — a cascade in the database would delete another
module's rows behind its back — so every module with per-user data must delete it **before** the
`users` row goes, and in the **same transaction**: an account must never be left half-deleted.

The original publishes `UserAccountDeletionRequestedEvent` and every module handles it with a plain
Spring `@EventListener`, which runs synchronously inside the publisher's transaction. Other events
(for example `UserRegisteredEvent`) use `@TransactionalEventListener(AFTER_COMMIT)` instead.

## Decisions

**Events are records, handlers are `IDomainEventHandler<TEvent>`, publishing is
`IDomainEvents.PublishAsync`** — in Common, about 30 lines, no library. Handlers run synchronously,
in order, in the publisher's DI scope, and their exceptions reach the publisher. An event that other
modules handle lives in the publishing module's `Contracts` namespace.

**When handlers run is decided by where the publisher publishes, not by a flag on the handler.**
Inside a transaction, before the publisher's own change: handlers are part of the transaction
(Spring's `@EventListener`). After the save: handlers react to a committed fact
(`AFTER_COMMIT`). With explicit saves the order of statements says it, and a reader sees it in
one place.

**One transaction across modules is an ambient `TransactionScope` (`ModuleTransaction.Begin()`).**
Every module's DbContext enlists in it on its own, so a handler needs nothing from the publisher but
the event. Two settings matter: `ReadCommitted` (a `TransactionScope` defaults to `Serializable`,
which in Postgres turns ordinary concurrency into serialization failures) and async flow (without
it the transaction does not survive an `await`).

**No shared connection is needed — and none is used.** The first version gave every DbContext in a
request one shared connection, on the assumption that two connections in one `TransactionScope`
escalate to a distributed transaction. Breaking the code on purpose showed the deletion test still
passing with separate connections: Npgsql hands the same physical connection to connections opened
one after another inside one transaction. The shared connection was removed — it was unnecessary,
and it would have broken any request that runs two queries in parallel. What remains true: work
inside a `ModuleTransaction` must be sequential.

## Consequences

- A module with per-user data registers an `IDomainEventHandler<UserAccountDeletionRequested>`. If it
  does not, the first account deletion fails on a foreign key — loudly, and rolled back.
- Until other modules are ported, a test-only handler with its own DbContext stands in for them: it
  checks that handlers run while the account still exists, and that its own write is rolled back
  when it fails.
- `UserRegisteredEvent` and the other events of the original arrive with their first consumer; the
  mechanism is now in place for them.
