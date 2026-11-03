# ADR 0007: Background jobs

- Status: accepted
- Date: 2026-11-02

## Context

The original runs periodic work with Spring's `@Scheduled(fixedDelay = ...)`, switched on and off
with `@ConditionalOnProperty`: removing expired registrations and dead sessions now, and later
abandoned post images, the now-playing poller and the live-stream heartbeat. Identity has the
first two. The .NET counterpart should be written the same way every time, because the mistakes
it can hide only show up in production: an overlapping run, a connection held forever, or a
whole API stopped by a database that was unavailable for a moment.

## Decisions

**A job is a `BackgroundService` with a `PeriodicTimer` on the injected `TimeProvider`.** It runs
once at startup, then once per interval. The loop is a single `await` chain, so two runs never
overlap: a run that takes longer than the interval is followed by the next one, not raced by it
(what `fixedDelay` gave the original). `PeriodicTimer` merges missed ticks into one, so a long
run is not followed by a burst.

**Every run gets its own DI scope.** A `DbContext` is scoped. Holding one in a singleton job would
mean one connection and one change tracker kept for as long as the process lives.

**A run never throws.** Since .NET 6, an exception escaping `ExecuteAsync` **stops the host**
(`BackgroundServiceExceptionBehavior.StopHost`). That is stricter than Spring, which logs an
exception from a scheduled method and runs it again next time. Without the catch, a three-second
database failover at night would stop the whole API, not one cleanup run. Each run catches
everything except cancellation, logs a warning and leaves the work to the next run. Cleanup jobs
are hygiene, not a critical path.

**The `Enabled` switch is read inside the job, not used to decide whether it is registered.** A
test can then switch the job on in an instance of its own (`factory.WithSettings`). The shared test
instance switches every job off: they run at startup, and they would race tests that insert
expired rows on purpose.

**Large deletes go in batches, and every batch is its own transaction.** The session cleanup
issues `Where(expired).OrderBy(expiry).Take(n).ExecuteDeleteAsync()`, which EF Core translates to
the original's native SQL, with an `ORDER BY` added:

```sql
DELETE FROM sessions AS s WHERE s.id IN (
    SELECT s0.id FROM sessions AS s0 WHERE s0.expires_at < $1 ORDER BY s0.expires_at LIMIT $2)
```

No transaction is opened around the loop, so each statement commits on its own and releases its
locks at once. The original had to put `@Transactional` on the repository method rather than on
the job for the same reason: one transaction around the whole run would make batching pointless.
`ORDER BY` makes the oldest rows go first, so a test can know which rows a limited run removes.
Without it, Postgres takes rows in the order they are stored on disk. A run also has a ceiling
(`MaxBatchesPerRun`), so a neglected table drains over several runs.

**No coordination between instances.** Every instance runs every job. The deletes are idempotent,
so two instances running at once only repeat work, the same as in the original. Leader election
would be machinery without a problem to solve.

## Consequences

- A new job copies this shape: `BackgroundService`, a `PeriodicTimer` on `TimeProvider`, a scope
  per run, a catch around the run, and an `Enabled` switch that is off in the shared test instance.
- A job's options are validated at startup when a wrong value would do harm rather than nothing.
  A negative session retention would put the cutoff in the future and log out users whose sessions
  are still valid.
- Tests of a job start an instance of their own and wait for its startup run. They assert only on
  their own rows, because the database is shared.
