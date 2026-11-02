# ADR 0001: Port the Songly backend to .NET, behaviour for behaviour

- Status: accepted
- Date: 2026-11-02

## Context

Songly already has a working backend: a Spring Boot modular monolith
([`dolek122/Songly-backend`](https://github.com/dolek122/Songly-backend)) with ~19k lines of
production code, ~640 tests, 33 migrations and 49 ADRs, used by the mobile app. This repository
rebuilds it on .NET 10, primarily as a portfolio project in the .NET ecosystem.

A port can go wrong in two ways. It can drift — endpoints that look the same but return slightly
different shapes, codes or messages, which breaks the existing mobile app. Or it can lose the
reasoning — the original's behaviour is full of deliberate, non-obvious decisions (privacy,
moderation, security), each with an ADR explaining why. Re-deriving them from the Java code alone
is how they get lost.

## Decision

Port the behaviour, not the code.

- **Same contract.** The original OpenAPI document is copied into `docs/api/openapi.reference.json`.
  Paths, status codes, field names, enum values, error codes and error messages (Polish, because
  the app displays them) must match it.
- **Same schema.** The original Flyway migrations run unchanged (ADR 0003).
- **The original ADRs remain the source of truth for behaviour.** Before porting a module, read its
  ADRs in the original repository. This repository's ADRs record only decisions that are specific
  to .NET or to the port itself.
- **Port module by module, tests included.** A module counts as ported when its endpoints exist and
  the scenarios of the original's tests for it pass here.

## Consequences

- The existing mobile app can be pointed at this backend without changes — the strongest
  available proof that the port is faithful.
- Writing idiomatic .NET is still the goal: the contract is fixed, the implementation is not.
  Where the platform offers a better mechanism than a line-by-line translation (for example,
  assembly boundaries instead of package-private visibility, ADR 0002), the port uses it.
- Some framework behaviour has to be reproduced on purpose, because ASP.NET Core and Spring differ
  (for example, which errors the framework answers with an empty body). Each such place is covered
  by an integration test.
