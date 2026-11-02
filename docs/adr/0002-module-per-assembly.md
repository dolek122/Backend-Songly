# ADR 0002: One assembly per module, internal by default

- Status: accepted
- Date: 2026-11-02

## Context

Songly is a modular monolith: one deployable application, internally split into domain modules
(identity, profile, social, content, ...) that talk to each other only through explicit interfaces
and domain events. The original Java backend keeps those boundaries with package-private
visibility plus six ArchUnit rules:

1. A module reaches another one only through its public interfaces and events.
2. No cycles between modules.
3. `common` knows nothing about the domain.
4. JPA entities are not public.
5. Repositories are not public.
6. Controllers do not see entities.

C# has no package-private visibility. Its equivalent unit of encapsulation is the assembly.

## Decision

Every module is its own project (assembly). Inside it, everything is `internal` by default. The
only public types are:

- the module's entry point — the class implementing `Songly.Common.Modules.IModule`, which the host
  uses to register services and map endpoints;
- the types in the module's `Contracts` namespace — interfaces, events and DTOs meant for other
  modules.

HTTP endpoints are Minimal APIs rather than MVC controllers, because MVC discovers only public
controllers; Minimal API handlers can stay internal.

Within a module, entities live in a `Domain` namespace and endpoints with their request/response
types in an `Http` namespace.

## How the rules are enforced

| Original rule | Enforced by |
|---|---|
| 1. Only through contracts | Compiler (`internal`) + architecture test |
| 2. No cycles | Compiler — project references cannot form a cycle |
| 3. Common knows no domain | Compiler — every module references Common, the reverse would be a cycle |
| 4, 5. Entities and repositories not public | Architecture test: nothing outside `Contracts` and the entry point is public |
| 6. Endpoints do not see entities | Architecture test: `*.Http` must not depend on `*.Domain` |

Two .NET-specific rules are added: `InternalsVisibleTo` may open a module only to its own test
assembly (otherwise it is a back door around `internal`), and every `IModule` implementation must
be registered in `ModuleCatalog` (a forgotten module would compile and silently do nothing).

Every architecture rule has been seen failing on a deliberate violation before being accepted.

## Alternatives considered

**A separate `*.Contracts` assembly per module.** Common in .NET modular monolith templates, and it
makes rule 1 entirely a compiler matter. Rejected because it allows two modules to depend on each
other's contracts — a cycle the original deliberately forbids (rule 2; for example, `profile` must
not depend on `social` because `social` depends on `profile`). It would also double the number of
projects.

## Consequences

- The compiler is the first line of defence, as in the original: breaking a boundary usually does
  not even compile.
- Module dependencies are visible as project references, so the dependency graph can be read from
  the `.csproj` files.
- Extracting a module into a separate service later (original Claude.md §10) starts from an
  assembly that already has an explicit public surface.
