# Songly — .NET backend

A .NET 10 port of the original Spring Boot backend,
[`dolek122/Songly-backend`](https://github.com/dolek122/Songly-backend). Same REST contract, same
database schema, so the existing mobile app works against either. Built as a portfolio project:
the owner has to be able to explain every part of it in a job interview, so prefer clear,
idiomatic, well-reasoned code over clever code, and keep changes reviewable (one module or one
concern per change).

## Source of truth

- **Behaviour:** the original repository — its `CLAUDE.md` (in Polish) and `docs/adr/` (49 ADRs).
  Before porting a module, read the original module's code, the ADRs it references and its tests.
  The behaviour is full of deliberate, non-obvious decisions (privacy, moderation, anti-abuse);
  porting the code without the reasoning is how they get lost.
- **Contract:** `docs/api/openapi.reference.json`. Paths, methods, status codes, field names, enum
  values, error codes and error messages must match. A difference is a bug in the port.
- **Schema:** `src/Songly.Migrations/Scripts` — the original Flyway scripts, verbatim.
- **This repository's ADRs** (`docs/adr/`) record only .NET-specific decisions. Add one when a
  decision would make a reader ask "why is it done this way?".

## Commands

```bash
dotnet build Songly.slnx
dotnet test                                   # needs Docker (Testcontainers)
dotnet format Songly.slnx --verify-no-changes # CI fails on unformatted code
docker compose up -d postgres redis minio minio-init && dotnet run --project src/Songly.Api
dotnet run --project src/Songly.Api --launch-profile local  # + test accounts (admin@songly.local, user@songly.local / Songly123!)
```

The repo uses Microsoft.Testing.Platform (`global.json`). Filter with
`dotnet test --project tests/Songly.IntegrationTests --filter-method "*Name*"`.

## Module rules (ADR 0002)

- One module = one project under `src/Modules`. Everything is `internal` except the `IModule`
  implementation and types in the `Songly.<Module>.Contracts` namespace.
- Inside a module: entities in `Domain/`, endpoints with their request/response records in
  `Http/`, the rest (services, persistence) where it reads best. `Http` must not depend on `Domain`
  — endpoints return DTOs, never entities.
- Another module is reached only through its `Contracts` (interfaces, events, DTOs) via a project
  reference. The dependency graph must stay acyclic — the compiler enforces it. If a dependency
  would create a cycle, the original solved it already; look at how.
- `InternalsVisibleTo` only to the module's own `<Module>.Tests` assembly.
- Register every new module in `ModuleCatalog`. A module that needs middleware implements
  `IModule.UsePipeline` (identity does: authentication, authorization).
- Each module has its own `DbContext` (`AddModuleDbContext<T>`), mapping only its own tables.
- Modules react to each other through domain events (`IDomainEvents`, `IDomainEventHandler<T>`,
  ADR 0006). Publish inside `ModuleTransaction.RunAsync` for "all or nothing" (handlers share the
  transaction), after the save for reactions to a committed fact. `UserRegistered` is published
  inside the account's transaction (ADR 0008); its handlers must not send anything outside.
- A handler inside a transaction that must change something outside the database (delete a file
  its deleted rows pointed to) defers it with `IAfterCommit.Defer` — it runs only after the commit
  (ADR 0010). Rows go inside the transaction, files after it.
- **A module with per-user data handles `UserAccountDeletionRequested`** and deletes that data —
  otherwise account deletion fails on a foreign key. Keep the "what we delete" list on the
  `/account-deletion` page (`AccountDeletionPages`) in step with those handlers.
- `Songly.Common` is infrastructure only and must not know any module.
- When adding an architecture rule, make it fail once with a deliberate violation first.

## Conventions

- Code, comments, docs and commit messages in English. **API error messages stay in Polish**,
  copied exactly from the original — the mobile app displays them.
- Expected failures are subclasses of `ApiException` (status + code + message). Anything else that
  escapes an endpoint is a logged 500. Never return ad-hoc error bodies or ProblemDetails.
- JSON: camelCase, instants as UTC with `Z` (`DateTimeOffset`), enums as `UPPER_SNAKE_CASE`
  strings — configured once in `AddSonglyWeb`.
- Enums are stored as their `UPPER_SNAKE_CASE` names (`EnumNameConverter<T>`), as the original
  wrote them. An enum that appears in a response gets its own copy in `Http` with an explicit
  mapping — endpoints never see `Domain` types, enums included.
- Time comes from the injected `TimeProvider`, never `DateTime.UtcNow`, so tests can control it.
- Logs: never log e-mail addresses, request bodies, passwords or tokens. Identify users by id.
- Migrations: never edit an applied script (DbUp keeps no checksums, ADR 0003). New changes are
  new scripts, `V34__...` onwards, in plain SQL.
- Endpoints are **closed by default**: the fallback policy requires the `USER` role. Public
  endpoints say `.AllowAnonymous()`; everything under `/api/v1/admin` is admin-only by path
  (ADR 0004). Read the caller's id with `ClaimsPrincipal.GetUserId()`.
- Request records validate with `Songly.Common.Validation` attributes (`NotBlank`, `Email`,
  `Size`, `MaxUtf8Bytes` — named after the original's Bean Validation). Validation runs for every
  endpoint automatically; declare the attribute, do not call anything. Put the attributes on
  properties (`init` properties, not positional record parameters).
- Side effects (e-mail, push) go **after** the save that makes them true — the order of
  statements is the "after commit" (ADR 0005). Send mail through `IMailSender` and pushes
  through `IPushSender`; a delivery failure must never change the HTTP response.
- Lists are paged with `PageQuery` (Spring's bounds: nothing rejected, size 20 by default, at
  most 100) and return `PagedResponse<T>` via `ToPageAsync` on an **ordered** query with the
  filters inside it (ADR 0009). A list only code can order (matching ranks by a computed score) is
  cut in memory and wrapped with `PagingExtensions.Page`. A required set of ids from the query
  string goes through `RequiredGuidSet` (`?ids=a&ids=b` and `?ids=a,b` alike — the mobile app sends the latter).
- Files go to `IObjectStorage`; keys are stored, never URLs (`PublicUrl(key)` builds the address).
  Uploaded images go through `SanitizedImage.From` first — format from content, metadata removed
  (ADR 0010). Upload the new file before the save, delete the old one after it.
- "At most once per interval" goes through `IIntervalGate` (Redis, fail-open). Never make a
  request depend on Redis being up.
- Periodic work is a `BackgroundService` with a `PeriodicTimer` on `TimeProvider`, a DI scope per
  run, a catch around every run (an escaping exception stops the whole host) and an `Enabled`
  switch, which the shared test instance turns off (ADR 0007).
- The `Local` environment (`appsettings.Local.json`) is the original's `local` profile. Anything
  with a password from the repository is switched on only there, and refuses to start where
  `ProductionSecretsRequired` is set.
- Warnings are errors. Fix the cause; suppress only with a comment saying why.

## Tests

- Integration tests run the whole application against real Postgres (Testcontainers), never an
  in-memory substitute. `SonglyApiFactory` is shared by the test assembly.
- Pure logic with no HTTP, database or other module involved (formatting, calculations) gets unit
  tests in `tests/Songly.<Module>.Tests` — the only assembly the module's internals are visible to.
- Create accounts with `factory.CreateUserAsync()` and log in with `client.LoginAsync(user)`.
  It registers through the API (ADR 0008), so the account has a profile, a session and a display
  name equal to the part of its address before the "@"; the verification e-mail is forgotten by
  the recorder. Use `factory.ExecuteSqlAsync` only for state the API cannot produce yet or whose
  age is the subject of the test. Every test makes its own accounts — the database is shared.
- Read tokens from the e-mail the app sent (`factory.Mails.TokenSentTo(email)`), never from the
  database. Pushes are recorded per device token (`factory.Pushes.SentTo(token)`); register a
  device with a unique token per test. A test needing other settings runs against `factory.WithSettings(...)` — a second
  app instance on the same database and Redis. Background jobs are off in the shared instance.
- Data that must not reach the shared database (fixed e-mail addresses, an extra administrator)
  goes to a `TemporaryDatabase` of its own: `factory.WithSettings(database.Settings)`. The same for
  state that is global to the installation (the genre popularity snapshot): a temporary database
  plus a Redis database of its own (`,defaultDatabase=N`), or parallel tests evict it mid-test.
- Another module's contract is tested the way that module will use it: resolve it from a scope of
  `factory.Services` (e.g. `IProfileDirectory`).
- Uploaded files are checked in the real MinIO by fetching `factory.StoragePublicBaseUrl/...`
  anonymously, as clients do. Test images come from `tests/Shared/TestImages.cs` (no image library:
  compare results byte for byte).
- Run a new test class on its own too (`--filter-class`): passing only as part of the full suite
  means it depends on another test having run first.
- When porting a module, port the scenarios of the original's tests for it — they are the
  executable specification of the behaviour.
- A test that cannot fail proves nothing. For a new safeguard, break the code once and watch the
  test go red.
