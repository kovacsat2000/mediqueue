# CLAUDE.md — MediQueue

Instructions for Claude Code working in this repository. Read this before making any change.

---

## What this project is

**MediQueue** is a client–server system for a multi-doctor medical practice. Assistants register patients and route them to a specialty; the server assigns the patient to a doctor's waiting list; doctors call patients in, record diagnoses, and release them. Every state change is pushed to all connected desktop clients in real time, every data modification is audited, and access is role-based.

It is a proof of concept built to be **presented and defended live, line by line**. That single fact governs everything below: prefer the proportionate solution over the clever one, and never introduce something that cannot be explained in one sentence.

---

## Non-negotiables

1. **Everything in this repository is in English.** Code, identifiers, comments, commit messages, documentation, UI labels, test names. The only exception is *seed data values* (patient and doctor names, addresses, specialty names), which are Hungarian so the demo reads naturally.
2. **No time-based scoping.** Never propose cutting, deferring, or timeboxing work because of time. Time is not a constraint in this project. If something should be built, build it.
3. **Never invent version numbers or API shapes from memory.** Check the actual installed SDK, the actual package version, and the actual API before using it. If a package's current version is unknown, add it with `dotnet add package <name>` and let NuGet resolve, then record the resolved version in `Directory.Packages.props`.
4. **Do not silently change a decision recorded in `context/decisions.md`.** If a decision turns out to be wrong, stop, explain why, and let the controller session re-decide.
5. **Do not commit secrets.** Development JWT signing keys and seed passwords live in `appsettings.Development.json` and are documented in the README as demo credentials — that is intentional and fine. Nothing else.

---

## Environment

| | |
|---|---|
| OS | macOS Tahoe 26.x, Apple Silicon (`osx-arm64`) |
| Shell | zsh |
| Package manager | Homebrew (`/opt/homebrew`) |
| .NET SDK | **10.0.x** — pinned in `global.json` |
| IDE | VS Code + C# Dev Kit (Visual Studio for Mac is retired; Rider is not used) |
| Container runtime | Docker Desktop |
| Database | PostgreSQL 17 in Docker, never installed on the host |

`DOTNET_ROOT` must point at the version-independent Homebrew symlink, not a Cellar path:

```bash
export DOTNET_ROOT="$(brew --prefix)/opt/dotnet/libexec"
```

---

## Commands

```bash
# --- database -------------------------------------------------------------
docker compose up -d db            # start PostgreSQL
docker compose down                # stop
docker compose down -v             # stop and wipe the volume (fresh seed)

# --- build & test ---------------------------------------------------------
dotnet restore
dotnet build                       # warnings are errors — a warning is a failure
dotnet test                        # all test projects
dotnet test tests/MediQueue.Domain.Tests

# --- run ------------------------------------------------------------------
dotnet run --project src/MediQueue.Api                   # API + Swagger UI
dotnet run --project src/MediQueue.Client.Assistant      # assistant desktop app
dotnet run --project src/MediQueue.Client.Doctor         # doctor desktop app

# --- EF Core (dotnet-ef is a pinned local tool) ---------------------------
dotnet tool restore
dotnet ef migrations add <Name> \
  --project src/MediQueue.Infrastructure \
  --startup-project src/MediQueue.Api
dotnet ef database update \
  --project src/MediQueue.Infrastructure \
  --startup-project src/MediQueue.Api

# --- formatting -----------------------------------------------------------
dotnet format                      # run before every commit
```

---

## Architecture

```
Domain        → (nothing)                          entities, value objects, state machine, rules
Contracts     → (nothing)                          DTOs and wire enums, shared with the clients
Application   → Domain, Contracts                  use cases, service interfaces, authorization rules
Infrastructure→ Application, Domain, Contracts     EF Core, migrations, audit interceptor, auth, SignalR
Api           → Application, Infrastructure*       controllers, hubs, composition root, OpenAPI
Client.Core   → Contracts                          HTTP client, SignalR client, auth state, view models
Client.*      → Client.Core, Contracts             Avalonia desktop shells
```

\* `Infrastructure` is referenced from `Api` **only** in the composition root (`Program.cs` / DI extension methods). Controllers depend on Application interfaces, never on Infrastructure types.

**The dependency rule is the point.** If you find yourself wanting to reference Infrastructure from Application, or EF Core from Domain, stop — introduce an interface in the inner layer and implement it in the outer one.

---

## Rules that are easy to break

These come from the specification and from the decision log. Violating any of them is a bug, not a style issue.

1. **`VisitSummaryDto` must never gain a diagnosis property.** It is the type used by every assistant-facing endpoint *and every SignalR payload*. The absence of the field is the security mechanism. Diagnoses travel only in `VisitDetailDto`, only from doctor-scoped endpoints.
2. **A doctor may only touch their own queue.** Enforce it server-side on every doctor endpoint, not in the client.
3. **An assistant may never register through a doctor endpoint and a doctor may never register a patient.** Both are policy-enforced.
4. **The state machine has exactly four states and exactly three transitions.** Do not add a fifth state. Deletion is a soft-delete flag, orthogonal to status.
5. **Every state transition goes through `VisitStateMachine`.** Never set `Visit.Status` directly from a service.
6. **Audit is captured in the EF Core `SaveChangesInterceptor`,** never by application services calling an audit method. If something is not being audited, fix the interceptor.
7. **Sensitive audit values are redacted for assistants** (`***` plus a `redacted: true` flag), never omitted silently.
8. **All timestamps are `DateTimeOffset` in UTC.** Formatting for display is the client's job.
9. **All primary keys are `Guid.CreateVersion7()`.** Never `Guid.NewGuid()`.

---

## Build hygiene established in P0 — do not undo

- **`nuget.config` at the root clears inherited package sources.** This machine has a private Azure Artifacts feed in its user-level config; central package management fails to resolve across multiple sources. Do not remove the `<clear />`.
- **`Microsoft.OpenApi` is pinned forward to 2.7.5** to close a high-severity advisory that the transitive 2.0.0 carries. Do not relax the pin. Never suppress `NU1903`.
- **`.gitattributes` forces `eol=lf`.** The Avalonia templates emit CRLF; without this the history alternates.
- **`global.json` sets `allowPrerelease: false`.** Outside Visual Studio this defaults to true, which would silently select a preview SDK.
- **`app.UseHttpsRedirection()` applies outside Development only.** Under the http launch profile it logs a warning on every request, which pollutes the demo.
- **`EnforceCodeStyleInBuild` is effectively decorative** as configured: IDE analyzers default to *silent*, and raising their severity makes the stock templates fail to compile. `dotnet format` is the real gate. Do not raise IDE severities without a decision from the controller session.

---

## Testing

- **xUnit v2 (2.9.3) + NSubstitute + Shouldly.** Not FluentAssertions — v8+ requires a paid commercial licence. xunit.v3 was considered and declined (see `context/decisions.md` D-22); do not migrate without a controller decision.
- **Domain tests are pure**: no database, no mocks of infrastructure, no async.
- The state machine test asserts **all sixteen ordered `(from, to)` pairs**, allowed and denied. Do not shorten it to the happy path.
- Validation tests cover: valid input, empty, digits in the name, wrong TAJ format, and the checksum rule both enabled and disabled.
- **Integration tests** use `WebApplicationFactory` with Testcontainers running real PostgreSQL. No in-memory provider — it hides provider-specific behaviour, which defeats the purpose.
- A new business rule without a test is not finished.

---

## Git

- The repository **is** the submission; the commit history is visible to the evaluators. Write it accordingly.
- Small, scoped commits. One logical change per commit.
- Conventional-commit style subjects in English, imperative mood:
  `feat(domain): add visit state machine with exhaustive transition table`
  `test(domain): cover all 16 ordered state transition pairs`
  `fix(api): return 409 with valid alternatives on invalid transition`
- Never force-push to `main`. Never rewrite pushed history.
- Run `dotnet format && dotnet build && dotnet test` before every commit. CI runs `dotnet format --verify-no-changes` as a build step, so an unformatted commit turns the pipeline red.
- **Every commit must build.** In particular: a project is added to `MediQueue.sln` in the *same* commit that creates its `.csproj`. Never stage solution entries ahead of the projects they reference — that produces a commit that cannot restore, and it is exactly how the one defective commit in the P0 history happened.
- Before committing a series, sanity-check it: `git stash` any work in progress, then build the intermediate states if a commit reorders or splits files across projects.

---

## Context files

The controller session maintains these in `context/` (gitignored — they are not part of the submission):

| File | Contents |
|---|---|
| `context/handover.md` | Founding context: full task specification, constraints, company and evaluation context |
| `context/plan.md` | Stack, architecture, data model, API surface, phase breakdown |
| `context/decisions.md` | Decision log with rationale — **authoritative** over `plan.md` |
| `context/progress.md` | Phase status board |
| `context/diary.md` | Session log |

Read `context/plan.md` and `context/decisions.md` before starting any phase. If a phase brief contradicts them, say so rather than guessing.

---

## Working model

- Work proceeds in **phases**. Each phase arrives as a self-contained brief with a goal, scope, file list, and acceptance criteria.
- **Do only what the brief asks.** If something outside the scope looks broken or missing, note it in the report instead of fixing it — the controller session decides what happens next.
- **Report at the end of every phase**: what was built, what deviated from the brief and why, what is broken, what the next phase needs to know.
- If a brief turns out to be wrong or impossible, stop and say so. Do not improvise around it.
