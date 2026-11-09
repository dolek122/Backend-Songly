# Songly — backend (.NET)

[![CI](https://github.com/dolek122/Backend-Songly/actions/workflows/ci.yml/badge.svg)](https://github.com/dolek122/Backend-Songly/actions/workflows/ci.yml)

Songly is a social app built around music: posts, likes and comments, friends, live "now playing"
status from Spotify, genre-based matching and topic communities with their own moderation.

This repository is a **.NET 10 port of the original Spring Boot backend**
([`dolek122/Songly-backend`](https://github.com/dolek122/Songly-backend)). It keeps the same REST
contract and the same database schema, so the existing mobile app
([`dolek122/Songly-mobile`](https://github.com/dolek122/Songly-mobile)) works against either one.
The original's architecture decision records remain the source of truth for *what* the system
does; this repository documents *how* it is done in .NET ([ADR 0001](docs/adr/0001-port-to-dotnet.md)).

## Status

| Module | Responsibility | Status |
|---|---|---|
| Common | Error format, request correlation, JSON conventions, validation, paging, e-mail channel, Redis interval gates, domain events, cross-module transactions with work after the commit, object storage, image metadata removal, push channel, rate limiting (per client address on the public paths, per user on writes, Redis windows shared by every instance, the real address behind a proxy) | ✅ |
| Migrations | Schema (33 original migrations) | ✅ |
| Identity | Registration with e-mail verification, login, JWT + rotated refresh tokens with theft detection, password change and reset, live roles, suspensions with appeals, admin roles, account deletion (API and web page), session cleanup, local test accounts | ✅ |
| Profile | Profiles created with the account, favourite genres, user search, public profiles hidden for suspended accounts, names for the administration, avatars stripped of metadata, genre popularity cached in Redis, the directory other modules read names and genres from | ✅ |
| Social | Friend requests (mutual ones become a friendship), friends, blocking that ends the relation, block state for the profile screen, the graph other modules read friends and blocks from | ✅ |
| Content | Posts, likes, comments, shares, the feed, posts in communities with moderation of posts and comments, content of blocked and suspended authors hidden on every path, post images through a register of uploads (stripped of metadata, abandoned ones cleaned up), reports to community moderation with a per-reporter limit that depends on their track record | ✅ |
| NowPlaying | Own status, the manual fallback for services without an API, friends' statuses from Redis, visibility setting, suspended accounts left out, Spotify through OAuth (callback back to the app, tokens encrypted with AES-GCM, revoked consent drops the link), the friends' statuses live (SSE through a Redis channel), a server-side poll of the watched users with a shared backoff | ✅ |
| Matching | Genre-based matching: a weighted Jaccard (a shared rare genre counts for more), damped by how much both profiles tell, with the confidence next to the score; friends, blocked and suspended users left out | ✅ |
| Community | Communities, membership, roles (moderators named by the owner), bans (temporary or permanent), appeals to the moderators and escalation to the administration, the membership contract for content | ✅ |
| Notification | Inbox from other modules' events (likes, comments, shares, friend requests, joins, role changes), hidden from blocked users without deleting anything, undo/redo loops deduplicated; moderation decisions to both sides through blocks and without the moderator's name; e-mail for those decisions and for suspensions, with mail preferences and an interval for moderators' mail; push through FCM for every new entry, with devices unpinned together with the sessions | ✅ |
| Analytics | Counters of domain events without personal data — activity and moderation decisions (bans split by term, appeals and their outcomes, posts entering the report queue) — and an admin-only summary over a window of days; each count also a metric | ✅ |
| ModerationLog | Append-only history of moderation decisions — community bans and their lifting, removed posts (with how many reports they had) and comments, reports upheld and dismissed per reporter, appeals and escalations, moderator roles, suspensions and their appeals, the administrator role — for a community's moderators and, per person, for the administrators; without reasons or appeal texts | ✅ |

## Architecture

A **modular monolith**: one deployable application, split into domain modules with explicit
boundaries, so that a module can be extracted into a service later without rewriting the rest.

```
src/
├── Songly.Api/            host: composition root, pipeline, health, metrics, OpenAPI
├── Songly.Common/         infrastructure only — knows nothing about the domain
├── Songly.Migrations/     original SQL migrations + DbUp runner
└── Modules/
    ├── Songly.Identity/   one assembly per module
    ├── Songly.Profile/
    └── ...
tests/
├── Songly.ArchitectureTests/   module boundary rules (ArchUnitNET)
├── Songly.IntegrationTests/    the whole app against real Postgres and Redis (Testcontainers)
├── Songly.Common.Tests/        unit tests of Common's internals
├── Songly.Identity.Tests/      unit tests of one module's internals
├── Songly.Profile.Tests/
└── Songly.Social.Tests/
```

**Each module is its own assembly, and everything in it is `internal`** except its entry point
(`IModule`) and its `Contracts` namespace. Another module cannot touch an entity, a repository or a
service — the compiler refuses. Cycles between modules are impossible because project references
cannot form one. What the compiler cannot check, the architecture tests do: no public type outside
`Contracts`, no `InternalsVisibleTo` back doors, endpoints never see entities, every module
registered. Each rule was seen failing on a deliberate violation before it was accepted
([ADR 0002](docs/adr/0002-module-per-assembly.md)).

**The schema is not regenerated from an ORM model.** The original Flyway scripts run verbatim
through DbUp, ordered by version, under a Postgres advisory lock
([ADR 0003](docs/adr/0003-migrations-with-dbup.md)).

**One error format for everything**, including errors the framework produces on its own (unknown
route, wrong method, malformed JSON, a non-UUID in the path):

```json
{ "code": "NOT_FOUND", "message": "Nie znaleziono zasobu: /api/v1/nope", "timestamp": "2026-11-02T12:00:00.123Z", "path": "/api/v1/nope" }
```

Messages are in Polish on purpose — the mobile app displays them.

**Closed by default.** Every endpoint requires a full, non-suspended account unless it opts out;
roles are read from the database on every request, so suspending an account or revoking an admin
role takes effect immediately; the whole admin area is guarded by path. A stolen refresh token
is detected the moment either copy is reused, and every session of the account is revoked
([ADR 0004](docs/adr/0004-authentication-and-authorization.md)).

**Creating and deleting an account are each one transaction across modules.** Every module
deletes its own data in reaction to a domain event, inside the same database transaction and before
the account row goes; if any module fails, nothing is deleted
([ADR 0006](docs/adr/0006-domain-events-and-cross-module-transaction.md)). The same holds for a new
account: its profile is created in the transaction that creates it, so there is never an account
without a profile ([ADR 0008](docs/adr/0008-new-account-event-in-the-accounts-transaction.md)).

**No oracle for who has an account.** Registering a free address and a taken one returns the
same response in the same time; the only difference is the e-mail the mailbox owner receives (a
confirmation link or a warning). The account does not exist until the address is confirmed. Mail
goes out from a background queue, so the response never waits for the mail server
([ADR 0005](docs/adr/0005-mail-redis-and-side-effects.md)).

**Background jobs never take the API down.** Cleanup runs in batches, each committed on its own,
and a failed run is logged and retried at the next interval — in .NET an exception escaping a
background service would stop the whole application ([ADR 0007](docs/adr/0007-background-jobs.md)).

**Uploaded photos lose their metadata.** The format is recognised by content (never by the file
name), and the file is rewritten without EXIF, GPS, XMP or thumbnails — only the orientation
survives — before it reaches the storage. A file nothing points to any more is deleted only after
the transaction that orphaned it commits ([ADR 0010](docs/adr/0010-object-storage-uploads-and-work-after-the-commit.md)).

**Every request is correlated.** It gets an id (or keeps a sanitised one from the client), returned
in `X-Request-Id` and attached to every log line, so a user's bug report leads to exactly their
request.

## Tech stack

.NET 10 · ASP.NET Core Minimal APIs · EF Core · PostgreSQL 16 · Redis · S3 / MinIO · DbUp · JWT bearer auth ·
bcrypt · MailKit · Serilog (JSON logs) · OpenTelemetry metrics (Prometheus) ·
OpenAPI + Scalar · xUnit v3 · Testcontainers · ArchUnitNET · Docker · GitHub Actions

## Running locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and Docker.

```bash
docker compose up -d postgres redis minio minio-init
```

```bash
dotnet run --project src/Songly.Api
```

The API listens on `http://localhost:8080`:

- `/scalar` — interactive API documentation
- `/openapi/v1.json` — OpenAPI document
- `/health` — health check (includes the database)
- `/metrics` — Prometheus metrics (administrators only)

To click through the mobile app on an Android emulator, run the `Local` environment instead:

```bash
dotnet run --project src/Songly.Api --launch-profile local
```

It creates two test accounts at startup, both with the password `Songly123!`:
`admin@songly.local` (administrator) and `user@songly.local`. A restart leaves them as they are.
The emulator reaches the API at `http://10.0.2.2:8080`. The passwords are in this repository, so
the accounts exist only in this environment, and an environment that requires real secrets
(`Songly:Security:ProductionSecretsRequired=true`) refuses to start with them. The profile listens
on `localhost` only, so the known admin password is not reachable from the local network.

Without the .NET SDK, the whole stack runs in Docker:

```bash
docker compose --profile app up --build
```

## Deploying

[`render.yaml`](render.yaml) is a [Render](https://render.com) blueprint: the application from the
`Dockerfile`, a PostgreSQL database and a Redis instance. Settings are environment variables named after
configuration keys (`Songly__Security__Jwt__Secret` is `Songly:Security:Jwt:Secret`); the secrets are asked
for when the blueprint is applied, never stored in the file. Render hands out the database as a
`postgresql://` URL, which the application accepts as well as Npgsql's own form.

The blueprint declares `Songly__Security__ProductionSecretsRequired=true`, so the instance refuses to
start with the public development secrets from `appsettings.json`, and switches on the forwarded headers,
so the rate limiter sees clients' addresses rather than Render's proxy.

## Tests

```bash
dotnet test
```

Integration tests start PostgreSQL, Redis, MinIO and (for the SMTP test) a test mail server in
containers, so Docker must be running. They run against the
real database on purpose — an in-memory substitute would lie about how Postgres behaves.

CI (GitHub Actions) checks formatting (`dotnet format`), builds with warnings as errors, runs all
tests and builds the Docker image on every push and pull request.

## Documentation

- [`docs/adr`](docs/adr) — decisions specific to this .NET implementation
- [`docs/api`](docs/api) — the API contract inherited from the original backend
- [Original ADRs](https://github.com/dolek122/Songly-backend/tree/main/docs/adr) — the reasoning
  behind the system's behaviour (in Polish)
