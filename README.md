# MediQueue

A client–server system for a multi-doctor medical practice. Assistants register
patients and route them to a specialty; the server assigns each patient to a
doctor's waiting list; doctors call patients in, record a diagnosis, and release
them. Every state change is pushed to the connected desktop clients in real
time, every data modification is audited, and access is role-based — an
assistant can see that a diagnosis was recorded, but never what it says.

> This README is a placeholder covering what exists today. It is rewritten in
> full — architecture, decisions, demo credentials and a walkthrough — once the
> system is complete.

## Status

Foundations only. The solution builds, tests and runs; there is no business
logic yet. The API exposes a health check and an OpenAPI document, and both
desktop clients open an unmodified template window.

## Prerequisites

| | |
|---|---|
| .NET SDK | 10.0.302 or a later 10.0.x — pinned in `global.json` |
| Docker | Docker Desktop, for the PostgreSQL container |

On macOS with a Homebrew-installed .NET, point `DOTNET_ROOT` at the
version-independent symlink so it survives an upgrade:

```bash
export DOTNET_ROOT="$(brew --prefix)/opt/dotnet/libexec"
```

## Running it

```bash
# Start PostgreSQL 17 (nothing reads it yet; it is here so the container
# story is proven from the first commit).
docker compose up -d db

# Build and test the whole solution.
dotnet restore
dotnet build
dotnet test

# Run the API — http://localhost:5123
dotnet run --project src/MediQueue.Api

# Run either desktop client.
dotnet run --project src/MediQueue.Client.Assistant
dotnet run --project src/MediQueue.Client.Doctor
```

With the API running in Development:

| Endpoint | |
|---|---|
| `GET /health` | `{ "status": "healthy", "version": "…", "utc": "…" }` |
| `/scalar/` | Browsable API reference |
| `/openapi/v1.json` | OpenAPI 3.1 document |

Stop the database with `docker compose down`, or `docker compose down -v` to
discard the volume and start from a clean database.

## The audit trail, and the one guarantee that is not structural

Every data modification is recorded — who, when, which record, and each field
that moved, from what to what. The capture happens in an EF Core
`SaveChangesInterceptor` rather than in each use case, **because a use case can
forget and this one must not**: a new use case is audited because it saves, and
a new entity is audited because it is not one of the two audit types themselves.
The entries are written in the same transaction as the change they describe.

The log is queryable at `GET /api/audit`, filterable by patient, by user and by
date, and it deliberately ignores the soft-delete filter — a deleted visit's
history is the history most worth having.

That creates the problem this section exists to name. The specification requires
an audit log that says what changed *and* forbids assistants from seeing
diagnoses. Together those hand the assistant the diagnosis through the back
door. So sensitive values are replaced with `***` for anyone who is not a
doctor, and the field change carries `redacted: true` so a client can render
"hidden" rather than three asterisks as though they were the data.

**This is the only security rule in the system enforced by a runtime branch
rather than by a type.** Everywhere else the guarantee is structural: an
assistant receives `VisitSummaryDto`, which declares no diagnosis member, so the
leak is not merely forbidden but unwriteable. That mechanism cannot reach here —
a doctor and an assistant read the *same* audit entry, and the same field must
be present for one and withheld from the other. No shape of type decides that.

What compensates, stated plainly because a reviewer is entitled to check it:

1. **The branch is written once**, in `AuditMapper.Reveal`. If redaction
   appeared in two methods, one of them would eventually be edited and the other
   would not.
2. **It is pinned by tests that read the raw JSON**, not a deserialised object —
   deserialising into `AuditFieldChangeDto` would discard the very field that
   leaked and pass cheerfully against a broken server. One of those tests sweeps
   every page of a multi-page response, because a leak on page three is still a
   leak.
3. **It fails closed.** The rule is written as "only a doctor may see this",
   not "an assistant may not", so an unrecognised or absent role is redacted
   until somebody decides otherwise.

Doctors do see the values. The specification's role split is about *assistants*
not seeing diagnoses, and clinical staff reading a patient's history is what a
medical record is for.

Two smaller decisions that follow from the same reasoning: seed rows are written
with auditing explicitly suppressed, because fixture is not history and a log
whose first two dozen entries have no actor teaches the reader that "no actor"
is normal — and a change whose actor genuinely cannot be determined is recorded
anyway, with a warning, because an anonymous entry is worth far more than a
missing one.

## Solution layout

```
src/
  MediQueue.Domain            entities, value objects, state machine, rules
  MediQueue.Contracts         DTOs and wire enums, shared with the clients
  MediQueue.Application       use cases and the interfaces outer layers implement
  MediQueue.Infrastructure    EF Core, audit interceptor, auth, SignalR
  MediQueue.Api               controllers, hubs, composition root, OpenAPI
  MediQueue.Client.Core       HTTP and SignalR clients, auth state, view models
  MediQueue.Client.Assistant  Avalonia desktop app — assistant role
  MediQueue.Client.Doctor     Avalonia desktop app — doctor role
tests/
  MediQueue.Domain.Tests
  MediQueue.Application.Tests
  MediQueue.Api.IntegrationTests
```

`Domain` and `Contracts` reference nothing. That is the load-bearing part of the
structure: the business rules and the wire format stay testable without a
database or a web host, and no dependency can point inward from infrastructure.

## Technology

C# on .NET 10 (LTS), ASP.NET Core Web API with controllers, PostgreSQL 17 via
EF Core, SignalR for push, Avalonia UI 12 for the desktop clients, Serilog for
structured logging, and xUnit with Shouldly and NSubstitute for tests. The
reasoning behind each of these is written up in `docs/decisions.md`, which
lands with the finished system.

## Licence

Built as a take-home assignment. Not licensed for reuse.
