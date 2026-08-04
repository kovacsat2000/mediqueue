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
