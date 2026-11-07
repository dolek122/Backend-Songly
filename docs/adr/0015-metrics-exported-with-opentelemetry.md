# ADR 0015: Metrics exported with OpenTelemetry

- Status: accepted
- Date: 2026-11-07

## Context

The original records its own counters with Micrometer and exposes them, with Spring Boot's own HTTP and JVM
metrics, at `/actuator/prometheus`. That path is for `ROLE_ADMIN` only (its Claude.md §13).

In this repository the modules record the same counters with `System.Diagnostics.Metrics`, through
`IMeterFactory`. Until now nothing exported them, so they existed only inside the process.

## Decisions

**OpenTelemetry's Prometheus exporter, configured only in the host.**
- `Songly.Api/Metrics.cs` selects the meters and maps the endpoint. The modules and `Songly.Common` know
  nothing of OpenTelemetry: a meter is plain .NET.
- The meters exported are:
  - every meter of the application (`Songly.*`);
  - ASP.NET Core's own (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`), the
    counterpart of Spring's `http.server.requests`;
  - the runtime's (`System.Runtime`).
- Prometheus naming comes from the exporter, as it did from Micrometer: `songly.auth.login.attempts` is
  scraped as `songly_auth_login_attempts_total`. Dashboards written against the original keep working.
- `prometheus-net` would have done the same with a stable package. OpenTelemetry is the standard .NET
  libraries now instrument for, and moving to OTLP later (a collector, a hosted backend) is a change of
  exporter, not of instrumentation.
- The ASP.NET Core Prometheus exporter is still a beta package. It sits behind one line in one file, so the
  risk stays contained there.

**`/metrics`, administrators only.**
- The path is Prometheus' convention. `/actuator` is a Spring Boot name, and this port has `/health`
  rather than `/actuator/health` too. The path is not part of the API contract the mobile app uses.
- The metrics say how many logins fail, how many refresh tokens are reused and what the limiters refuse —
  a map for whoever is attacking. So the endpoint uses the same admin policy as the analytics summary.

## Consequences

- A Prometheus server scraping the endpoint needs an administrator's access token, as against the original.
  That is impractical with 15-minute tokens. A dedicated scrape credential, or a port reachable only inside
  the platform, belongs with deploying a Prometheus at all.
- The `Songly.*` wildcard means a new meter is exported without touching the host — as long as it keeps the
  naming.
