# ADR 0003: The original Flyway scripts, run by DbUp

- Status: accepted
- Date: 2026-11-02

## Context

The schema is the contract with the data (ADR 0001). The original backend evolves it with 33
hand-written Flyway migrations (`V1__identity_schema.sql` ... `V33__suspended_users_index.sql`).

The usual .NET route would be EF Core migrations generated from the model. That would mean
re-expressing the schema through C# mappings and trusting the generator to produce the same
tables, constraints and indexes — a second source of truth that can drift from the first without
anyone noticing.

## Decision

- The original scripts are copied **verbatim** into `src/Songly.Migrations/Scripts` and embedded in
  the assembly.
- [DbUp](https://dbup.readthedocs.io/) applies them at startup, each in its own transaction,
  recording applied scripts in `schema_versions`.
- Scripts are ordered by their numeric version. DbUp's default name ordering would run `V10__`
  before `V2__`; a script name without a version fails the migration instead of being sorted
  somewhere arbitrary.
- The run holds a Postgres advisory lock, as Flyway does, so instances starting at the same time do
  not apply a script twice.
- A failed migration stops the application from starting.
- EF Core (when modules start using it) maps onto the existing tables. `dotnet ef migrations` is
  not used.

## Consequences

- The schema is identical to the original's by construction, which an integration test checks
  (every script applied, versions unique and contiguous).
- New schema changes are new scripts (`V34__...`), written by hand in SQL.
- **Known cost:** unlike Flyway, DbUp keeps no checksums. Editing a script that has already been
  applied is silently ignored by databases that ran it. Applied scripts are therefore immutable;
  a fix is always a new script.
