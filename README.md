# MediQueue

A client–server system for a multi-doctor medical practice. Assistants register
patients and route them to a specialty; **the server** assigns each patient to a
doctor's waiting list; doctors call patients in, record a diagnosis and release
them. Every state change is pushed to the connected desktop clients in real
time, every data modification is audited with the acting user, and access is
role-based — an assistant can see *that* a diagnosis was recorded, and never
what it says.

Built as a proof of concept: C# on .NET 10, PostgreSQL, SignalR, and two
Avalonia desktop clients over a shared core.

---

## Running it

**Prerequisites**

| | |
|---|---|
| .NET SDK | 10.0.302 or a later 10.0.x — pinned in `global.json` |
| Docker | for the PostgreSQL 17 container; nothing is installed on the host |

On macOS with a Homebrew .NET, point `DOTNET_ROOT` at the version-independent
symlink so it survives an upgrade:

```bash
export DOTNET_ROOT="$(brew --prefix)/opt/dotnet/libexec"
```

**Start it**

```bash
# 1. the database
docker compose up -d db

# 2. the API — migrates and seeds on first run
dotnet run --project src/MediQueue.Api          # http://localhost:5123

# 3. either client, or both, in their own terminals
dotnet run --project src/MediQueue.Client.Assistant
dotnet run --project src/MediQueue.Client.Doctor
```

| | |
|---|---|
| API | `http://localhost:5123` |
| OpenAPI reference (Scalar) | `http://localhost:5123/scalar/` — Development only |
| OpenAPI document | `http://localhost:5123/openapi/v1.json` — Development only |
| Health | `http://localhost:5123/health` |
| Push channel | `ws://localhost:5123/hubs/queue` |

Migrations are applied and the practice is seeded automatically **in
Development only**. `docker compose down -v` wipes the volume and gives you a
fresh seed on the next start.

**Check it works, in one command**

```bash
python3 scripts/smoke_test.py
```

Standard library only — no `pip install`. It registers a patient, finds her in
the unrouted list, routes her, asserts she is in exactly one doctor's queue,
calls her in, records a diagnosis, releases her, then reads the audit log as
both roles and asserts the diagnosis is legible to one and `***` to the other.
Exits non-zero naming the first thing that did not hold.

### Developer commands

```bash
dotnet build                        # warnings are errors
dotnet test                         # 473 tests across five projects
dotnet format                       # CI runs --verify-no-changes

scripts/verify-history.sh           # every unpushed commit restores, builds, tests and starts
python3 scripts/smoke_test.py       # end-to-end against a running stack

dotnet list package --vulnerable --include-transitive
```

---

## Demo credentials

Every seeded account shares the password **`MediQueue123!`**. They are in
`appsettings.Development.json` and in this file deliberately: they unlock
nothing but a local container, and a demonstration nobody can sign in to is
worse than a documented password.

| Username | Name | Role | Specialty | |
|---|---|---|---|---|
| `horvath.anna` | Horváth Anna | Assistant | — | |
| `kiss.eva` | Kiss Éva | Assistant | — | |
| `kovacs.istvan` | Dr. Kovács István | Doctor | Belgyógyászat | |
| `nagy.peter` | Dr. Nagy Péter | Doctor | Belgyógyászat | shares a specialty with Kovács, so doctor selection has a visible choice to make |
| `szabo.maria` | Dr. Szabó Mária | Doctor | Bőrgyógyászat | |
| `toth.gabor` | Dr. Tóth Gábor | Doctor | Szemészet | |
| `farkas.judit` | Dr. Farkas Judit | Doctor | Reumatológia | **Inactive on purpose — sign-in will fail** |

**Dr. Farkas is deactivated deliberately.** She is the only rheumatologist, so
Reumatológia is a specialty with no available doctor — which is what makes the
"the system refuses and the patient is not lost" path reachable at all. Without
her, every routing attempt succeeds and two rules become unprovable.

Seed data is Hungarian so the demonstration reads naturally and the tests
exercise the character set the system actually receives. Everything else —
code, comments, documentation, UI labels — is English.

---

## Architecture

```
Domain         → (nothing)                       entities, value objects, state machine, rules
Contracts      → (nothing)                       DTOs and wire enums, shared with the clients
Application    → Domain, Contracts               use cases, service interfaces, application exceptions
Infrastructure → Application, Domain, Contracts  EF Core, audit interceptor, auth, SignalR hub
Api            → Application, Infrastructure     controllers, composition root, OpenAPI
Client.Core    → Contracts                       HTTP client, SignalR client, auth state, view models
Client.*       → Client.Core, Contracts          Avalonia desktop shells
```

**`Domain` and `Contracts` reference nothing.** That is the load-bearing part:
the business rules and the wire format stay testable without a database or a web
host, and no dependency can point inward from infrastructure.

Three consequences worth naming, because they are what the structure buys:

- **`Domain` reads no ambient state at all** — no clock, no random source.
  Timestamps are parameters. A banned-API analyzer fails the build on
  `DateTimeOffset.UtcNow` or `Guid.NewGuid()` inside that project, so it is
  proven rather than asserted.
- **`Application` may not reference a storage, transport or hosting type.** It
  gets `IPasswordHasher<T>`, `TimeProvider` and `ILogger<T>` — abstractions
  whose implementations arrive at the composition root — and not `DbContext` or
  `IServiceCollection`. Because `IServiceCollection` is excluded, application
  services are registered from the API project, which is the composition root
  and is supposed to know the composition.
- **`Infrastructure` is referenced from `Api` only in the composition root.**
  Controllers depend on Application interfaces, never on a `DbContext`.

The desktop clients get the same treatment one layer out. `Client.Core` has **no
Avalonia reference**, so every view model is unit-tested without starting a UI
framework, and the shells are thin.

---

## Technology choices

The assignment asks for these to be justified, and the reasoning was written
down before the code. `docs/decisions.md` has the full set with alternatives and
trade-offs; this is the summary.

| Choice | Why |
|---|---|
| **C# / .NET 10** | The role's stack. LTS, first-party SignalR, EF Core and the desktop story in one runtime. |
| **PostgreSQL 17** | Named in the assignment. Also supplies `xmin`, which becomes the optimistic-concurrency token at no storage cost. |
| **EF Core 10** | The migration story and the change tracker; the audit interceptor is built on the latter, which is what makes auditing impossible to forget. |
| **SignalR** | First-party push, with a client that runs in a desktop process. The requirement is real-time updates to *desktop* clients, which rules out most of the alternatives. |
| **Avalonia UI 12** | Cross-platform XAML on .NET 10. WPF is Windows-only and the development machine is macOS; MAUI's desktop story is weaker for this shape of application. |
| **JWT bearer, hand-rolled issuance** | The requirement is role-based access, not identity management. A dependency on Keycloak or Identity Server would be more infrastructure than the system it protects. |
| **xUnit + NSubstitute + Shouldly** | Shouldly rather than FluentAssertions, which requires a paid licence from v8. |
| **Scalar** | Renders the OpenAPI document the .NET 10 template already generates; Swashbuckle is no longer the default and would be a second generator. |

### Deliberately not used

This is where most of the reasoning lives.

- **Supabase / any BaaS.** It would have supplied auth, a database and real-time
  in an afternoon — and the assignment is a test of designing a server, so
  buying one answers a different question. The audit requirement in particular
  wants server-side interception, which is exactly the part a BaaS takes away.
- **MediatR.** At this scope it is indirection with no benefit; the pipeline
  behaviours it exists for are already covered by ASP.NET Core middleware and
  the EF Core interceptor.
- **AutoMapper.** Worse than useless here specifically: the role-scoped DTOs are
  a security boundary, and a security boundary should be explicit and
  reviewable, not convention-driven.
- **A generic `IRepository<T>`.** It re-exports `IQueryable` and leaks EF Core's
  semantics through a type parameter, so the abstraction abstracts nothing.
- **A web front end.** The assignment says desktop clients.
- **An in-memory database provider for tests.** It hides provider-specific
  behaviour, which is the thing the integration tests exist to catch.

---

## How this was built

Written with AI assistance, which the assignment permits and which is worth
describing plainly — because the interesting part is not the tool, it is the
method and what the method caught.

The work ran as a **controller/executor loop**: a written brief in, a written
report back, and a review that ruled on every deviation and every open question
before the next brief was issued. Nine phases, each one reported against its own
acceptance criteria. Every brief that turned out to be wrong was reported as
wrong rather than worked around — twice a brief asserted a mechanism that a
mutant then disproved.

Four things were gates rather than intentions:

- **Mutation testing per phase.** Change one thing, predict the kill count *and
  the named victims first*, then measure. It found three tests that were green
  and asserting nothing, two concurrency tests that could not observe the
  condition they were named for, and twice it killed a *premise* that the brief
  and the code comment shared.
- **Per-commit verification.** `scripts/verify-history.sh` checks that every
  commit restores, builds, passes **and starts**, and it was calibrated against
  two known-bad commits before its output was trusted.
- **A decision log written before the code**, with an admitted trade-off in
  every entry — the curated version is `docs/decisions.md`.
- **Warnings as errors, format as a CI gate**, and an analyzer that fails the
  build if the domain layer reads a clock.

And the record includes the failures, which is what makes the rest of it
credible: **two commits reached the pushed history broken**, one that cannot
restore and one that compiles perfectly and cannot start. Both are named in
this file. The second is why the verification rule says "and starts" — it was
written after that commit, not before it.

**The architecture and every decision in it are mine, and both are defensible
line by line.** That is the actual claim, and the repository is the evidence
for it: the decision log, the reports, the mutants, and a history that was not
tidied up.

## What is implemented

The third column is the one that matters: a requirement is not done because
somebody says so.

| Requirement | Where it lives | How it is proven |
|---|---|---|
| Patient registration, returning patients reused | `VisitRegistrationService` | Registering the same TAJ twice yields one `Patient` and two `Visit`s — integration test |
| The **server** assigns the doctor | `IDoctorAssignmentStrategy` → `ShortestQueueAssignmentStrategy` | Unit tests including every tie-break; the demo shows two doctors alternating |
| Four-state machine, three legal transitions | `VisitStateMachine` | All **sixteen** ordered `(from, to)` pairs asserted, allowed and denied |
| Invalid transition returns something useful | `ProblemDetailsExceptionHandler` | 409 carrying `currentStatus`, `attemptedStatus` and `allowedTransitions` as extension members |
| Role-based access, server-side | Policies + ownership check in the application service | A doctor touching another doctor's visit gets 403; mutation testing confirms the check is load-bearing |
| **An assistant never sees a diagnosis** | `VisitSummaryDto` declares no diagnosis member | Raw-JSON assertions — the key is *absent*, not null — on responses and on push payloads. A reflection test asserts `IAssistantApi` cannot express a request that returns one |
| Audit: who, when, what, from what to what | `AuditSaveChangesInterceptor` | Written from the EF change tracker in the same transaction; a use case cannot forget because it does not participate |
| Audit is queryable, redacted for assistants | `GET /api/audit`, `AuditMapper` | The same entry read by both roles: value for one, `***` and `redacted: true` for the other |
| Real-time push to all clients | `QueueHub`, `IRealtimeNotifier` | Integration tests over a real WebSocket; a doctor never receives another doctor's queue events |
| Two desktop clients, main flow end to end | `Client.Assistant`, `Client.Doctor` | Driven end to end against a running server before release |
| Soft delete | flag on `Visit`, plus a global query filter | A deleted visit is a 404 through the API and its history is still in the audit log |
| Optimistic concurrency | PostgreSQL `xmin` as a shadow property | Two writers, second one loses deterministically, 409 |

---

## Testing

**473 tests** across five projects, each with a rule about what it may touch.

| Project | Tests | May depend on |
|---|---|---|
| `MediQueue.Domain.Tests` | 180 | nothing — no database, no mocks, no async |
| `MediQueue.Application.Tests` | 86 | substituted collaborators only; no host, no database, no container |
| `MediQueue.Client.Core.Tests` | 81 | a stubbed HTTP handler and a fake clock; **no UI framework** |
| `MediQueue.Api.IntegrationTests` | 123 | `WebApplicationFactory` + real PostgreSQL in Testcontainers |
| `MediQueue.Client.Ui.Tests` | 3 | the only project that starts Avalonia, headless — see below |

There is no in-memory database provider anywhere. The integration suite starts a
real PostgreSQL 17 container, because a suite that passes against a fake and
fails against the real thing is worse than no suite.

### Mutation testing

Every phase changed one thing at a time and measured how many tests died. It
earned its place three times:

- It found **two tests that were green and asserting nothing** — they asserted a
  401 that was arriving for a different reason than the one under test.
- It found **two concurrency tests that could not observe the condition they
  named**, because the stub HTTP handler returned an already-completed task, so
  the code under test never yielded and nothing could interleave with it.
- Twice it **disproved a causal claim** that a specification and a code comment
  agreed on — a route constraint that did not do what its comment said, and an
  `IgnoreQueryFilters()` call that was inert. Both times the code was
  defensible and the stated reason was false.

The practice that makes it work is predicting the kill count *and the named
victims* before running, and recording the prediction. A prediction that matches
is evidence the code is understood; a mismatch is the finding.

---

### The one test that starts a UI framework

`MediQueue.Client.Ui.Tests` runs a headless Avalonia application to prove the
last link in the push chain: a callback arriving on a background thread reaches
a control Avalonia is binding to, on Avalonia's own thread, without an
exception. Everything before that link is covered without a window, and
`Client.Core` keeps its no-Avalonia rule — which is why this is a separate
project rather than a package added to the existing one.

It is also where a claim got corrected. Avalonia 12.1.1 does **not** refuse a
bound collection mutated from a background thread, measured with a realised
`ListBox` carrying a selection. So "the clients were one push away from a
crash", which an earlier version of this file said, was unproven rather than
false. The marshalling is still right — it is what the framework's contract asks
for — but the test asserts only what it can show.

## Known limitations and honest trade-offs

**Redaction is the one guarantee enforced by a branch rather than a type.**
Everywhere else, an assistant simply receives a type with no diagnosis member,
so the leak is unwriteable. That cannot work for the audit log: a doctor and an
assistant read the *same* entry and the same field must be present for one and
withheld from the other. So it is a runtime branch — written in exactly one
method, pinned by tests that assert on raw JSON rather than on a deserialised
object, and swept across every page of a paged response. It fails closed: the
rule is "only a doctor may", so an unrecognised role is redacted.

**The TAJ checksum ships disabled.** The statutory check digit is implemented
and independently tested, behind `Validation:TajChecksumEnabled`, off by
default. The assignment defines the acceptance rule as format-only and its own
example (`123-123-123`) fails the checksum. Silently tightening a rule the
customer specified is a product decision, not an engineering one — so the rule
is built and the switch is left in configuration.

**The server is not hosted anywhere, and that is a decision rather than an
unfinished job.** External hosting was the one optional bonus left unfilled. The
demonstration runs locally, so a hosted instance would add a live network
dependency inside a thirty-minute slot and buy nothing that the local path does
not already show. The system is nonetheless built deployment-ready: PostgreSQL
in Docker Compose, every address configuration-driven with no literal in any
client, no dependency on a local file path, and migrations that run on start.
What would be needed is a container image for the API and somewhere to run it.

**No refresh tokens, no password lifecycle, no key rotation.** Tokens last eight
hours and that is the whole session story.

**Migrations are applied at startup**, in Development only. A deployed system
applies them as a separate step so two instances starting at once cannot race.

**A push that fails after a commit leaves a client stale.** The write succeeded
and the caller gets its 201; the notifier logs a warning and returns, because
failing a committed action to report a failed notification is worse. The
recovery is the Refresh button, which is why it is still in the clients.

**Two commits in the pushed history are defective, and were deliberately not
rewritten.**

| Commit | What is wrong | Which stage catches it |
|---|---|---|
| `ef46375` | A project was added to the solution before the `.csproj` it references existed, so it **cannot restore** | `dotnet restore` |
| `7cb6923` | Restores and compiles perfectly; an interface was registered with no implementation, so the container **cannot be built and the API will not start** | the test run, which boots the host |

They stand because this repository *is* the submission and its history is
evidence: rewriting pushed history to look tidier is editing the evidence. The
second one is the more interesting failure — a build-only check passes it,
because composition is not a compile-time property. `scripts/verify-history.sh`
now restores, builds **and runs the full suite** for every commit in a range,
and it was calibrated against both of these before it was trusted.

---

## What I would do differently with more time

Ordered by what would matter most, not by effort.

**Make the audit log survivable.** Values are unbounded `text` and the table
grows without limit. A production system decides a retention policy, partitions
`AuditEntries` by `OccurredAt` and moves cold partitions out. It would also log
audit *reads*, not only writes, and replace the current all-or-nothing role
split with a break-glass model that records a justification — clinical staff
seeing everything is defensible for a practice of seven and not for a hospital.

**Serve the validation rules to the clients.** Today the clients submit and
render the server's per-field errors, so a mistyped TAJ costs a round trip. The
answer is not to copy the rules into the client — that creates a second
definition that can disagree — but to serve them from the one definition, so
there is still only one.

**Idempotency keys on registration.** A lost response is currently recovered by
the duplicate-open-visit rule refusing the retry, which fails safe but tells the
caller nothing useful. An `Idempotency-Key` header with a stored response is the
real answer.

**Patient record reconciliation.** A returning patient's record is reused
unchanged, so a corrected address typed at reception is silently ignored.
Overwriting on every registration is worse — it rewrites identity data with
whatever was typed today. The right answer is an explicit merge-and-confirm
flow, which is a product decision this PoC does not make.

**User administration.** `Deactivate()` and `Reactivate()` exist on the domain
with no endpoint in front of them, and there is no way to change a doctor's
specialty or add an account without a seed.

**Observability beyond a trace id.** OpenTelemetry traces and metrics, and
correlation across the client, the API and the push channel — a request that
fails in the desktop client currently ends at a trace id the user has to read
out.

**A read model for the queue screens.** The Patient/Visit split means patient
names for a queue come from a batched lookup assembled in application code. At
a larger scale the answer is a projection shaped for that screen, not a bigger
dictionary.

**More of the state machine.** No-show returning a patient to `Waiting`,
transfer between doctors, reopening a completed visit. Four states and three
transitions is what the specification describes; a real practice has more.

**One application with role-driven navigation** rather than two shells. Two was
chosen for demonstration legibility and it cost one duplicated sign-in view; a
product would share it.

**Client reconnection beyond SignalR's own.** Automatic reconnect restores the
socket but replays nothing, so the client refetches on reconnect. A real
strategy would resynchronise from a sequence number rather than refetching
everything.

---

## Repository layout

```
src/                     the system
tests/                   five test projects
docs/
  workplan.md            the Tech Lead work plan — a deliverable in its own right
  decisions.md           the decisions worth defending, with their trade-offs
  demo-script.md         the runnable demonstration script
scripts/
  verify-history.sh      every commit restores, builds, tests and starts
  smoke_test.py          end-to-end proof against a running stack
.github/workflows/ci.yml build, test and format-check on every push
docker-compose.yml       PostgreSQL 17
```

## Licence

Written for an interview assignment. No licence is granted.
