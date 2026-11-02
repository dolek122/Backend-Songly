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
docker compose up -d postgres redis && dotnet run --project src/Songly.Api
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
- `Songly.Common` is infrastructure only and must not know any module.
- When adding an architecture rule, make it fail once with a deliberate violation first.

## Conventions

- Code, comments, docs and commit messages in English. **API error messages stay in Polish**,
  copied exactly from the original — the mobile app displays them.
- Expected failures are subclasses of `ApiException` (status + code + message). Anything else that
  escapes an endpoint is a logged 500. Never return ad-hoc error bodies or ProblemDetails.
- JSON: camelCase, instants as UTC with `Z` (`DateTimeOffset`), enums as `UPPER_SNAKE_CASE`
  strings — configured once in `AddSonglyWeb`.
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
- Side effects (e-mail, later push) go **after** the save that makes them true — the order of
  statements is the "after commit" (ADR 0005). Send mail through `IMailSender`; a delivery
  failure must never change the HTTP response.
- "At most once per interval" goes through `IIntervalGate` (Redis, fail-open). Never make a
  request depend on Redis being up.
- Warnings are errors. Fix the cause; suppress only with a comment saying why.

## Tests

- Integration tests run the whole application against real Postgres (Testcontainers), never an
  in-memory substitute. `SonglyApiFactory` is shared by the test assembly.
- Create accounts with `factory.CreateUserAsync()` and log in with `client.LoginAsync(user)`;
  use `factory.ExecuteSqlAsync` only for state the API cannot produce yet or whose age is the
  subject of the test. Every test makes its own accounts — the database is shared.
- Read tokens from the e-mail the app sent (`factory.Mails.TokenSentTo(email)`), never from the
  database. A test needing other settings runs against `factory.WithSettings(...)` — a second
  app instance on the same database and Redis. Background jobs are off in the shared instance.
- Run a new test class on its own too (`--filter-class`): passing only as part of the full suite
  means it depends on another test having run first.
- When porting a module, port the scenarios of the original's tests for it — they are the
  executable specification of the behaviour.
- A test that cannot fail proves nothing. For a new safeguard, break the code once and watch the
  test go red.
