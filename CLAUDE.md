# CLAUDE.md — MediQueue

Instructions for Claude Code working in this repository. Read this before making any change.

---

## What this project is

**MediQueue** is a client–server system for a multi-doctor medical practice. Assistants register patients and route them to a specialty; the server assigns the patient to a doctor's waiting list; doctors call patients in, record diagnoses, and release them. Every state change is pushed to all connected desktop clients in real time, every data modification is audited, and access is role-based.

It is a proof of concept built to be **presented and defended live, line by line**. That single fact governs everything below: prefer the proportionate solution over the clever one, and never introduce something that cannot be explained in one sentence.

---

## Non-negotiables

1. **Everything in this repository is in English.** Code, identifiers, comments, commit messages, documentation, UI labels, test names. Two exceptions:
   - **Seed and test data *values*** — patient and doctor names, addresses, specialty names, complaints, diagnoses. These are Hungarian, so the demo reads naturally and so the tests exercise the character set the system actually receives. `"Kovács Anna"` as a test value is data, not prose.
   - **Legal citations** keep their official Hungarian designation (`1996. évi XX. törvény, 2. sz. melléklet`), the way any statute is cited untranslated. The surrounding explanation is English.
2. **No time-based scoping.** Never propose cutting, deferring, or timeboxing work because of time. Time is not a constraint in this project. If something should be built, build it.
   - **One carve-out:** `docs/workplan.md` is a *deliverable* describing a hypothetical team, and the assignment asks it for sequencing and effort. Estimates belong in that document. The rule above governs how *this* project is executed, not what a deliverable about a different project may contain.
3. **Never invent version numbers or API shapes from memory.** Check the actual installed SDK, the actual package version, and the actual API before using it. If a package's current version is unknown, add it with `dotnet add package PackageName` and let NuGet resolve, then record the resolved version in `Directory.Packages.props`.
4. **Do not silently change a decision recorded in `context/decisions.md`.** If a decision turns out to be wrong, stop, explain why, and let the controller session re-decide.
5. **Do not commit secrets.** Development JWT signing keys and seed passwords live in `appsettings.Development.json` and are documented in the README as demo credentials — that is intentional and fine. Nothing else.
6. **If a brief asks for something you cannot do, say so plainly.** Do not approximate it and report success. Screen capture and UI automation are not available in this environment; anything needing human eyes belongs to Attila and the brief should have said so.

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
dotnet run --project src/MediQueue.Api                   # API + Scalar UI at /scalar/
dotnet run --project src/MediQueue.Client.Assistant      # assistant desktop app
dotnet run --project src/MediQueue.Client.Doctor         # doctor desktop app

# --- EF Core (dotnet-ef is a pinned local tool) ---------------------------
dotnet tool restore
dotnet ef migrations add MigrationName \
  --project src/MediQueue.Infrastructure \
  --startup-project src/MediQueue.Api
dotnet ef database update \
  --project src/MediQueue.Infrastructure \
  --startup-project src/MediQueue.Api

# --- security & formatting ------------------------------------------------
dotnet list package --vulnerable --include-transitive
dotnet format                      # run before every commit

# --- history verification (run BEFORE pushing, D-63) ----------------------
./scripts/verify-history.sh        # every commit in @{upstream}..HEAD must restore, build, test and start
```

---

## Architecture

```
Domain        → (nothing)                          entities, value objects, state machine, rules
Contracts     → (nothing)                          DTOs and wire enums, shared with the clients
Application   → Domain, Contracts                  use cases, service interfaces, application exceptions
Infrastructure→ Application, Domain, Contracts     EF Core, migrations, audit interceptor, auth, QueueHub + notifier
Api           → Application, Infrastructure*       controllers, hub *mapping*, composition root, OpenAPI
Client.Core   → Contracts                          HTTP client, SignalR client, auth state, view models
Client.*      → Client.Core, Contracts             Avalonia desktop shells
```

\* `Infrastructure` is referenced from `Api` **only** in the composition root (`Program.cs` and the `AddInfrastructure` / `AddMediQueueAuthentication` extensions). Controllers depend on Application interfaces, never on Infrastructure types and never on `DbContext`.

Query services are split by **what is read**, not by which screen shows it: `VisitQueryService` reads visits (including the unrouted list), `QueueQueryService` reads queues. An unrouted visit is in nobody's queue, so it does not belong to the second one.

`VisitContextLoader` exists so that four services do not each grow the same three lookups. Its boundary: it loads **display names** the DTOs carry and the entities do not. Anything that makes a decision stays in the service — do not let it accumulate business logic because it is convenient to reach.

**The dependency rule is the point.** If you find yourself wanting to reference Infrastructure from Application, or EF Core from Domain, stop — introduce an interface in the inner layer and implement it in the outer one.

**What `Application` may reference (D-39).** A package only if every type used from it is an interface or abstract type whose implementation is supplied at the composition root, implying no storage, no transport and no hosting model.

| Type | Verdict |
|---|---|
| `IPasswordHasher<TUser>`, `TimeProvider` | allowed |
| `DbContext`, `IServiceCollection`, anything ASP.NET | rejected |

Because `IServiceCollection` is out, **application services are registered from `AddApplicationServices()` in `MediQueue.Api`** — the composition root, which is supposed to know the composition.

---

## Rules that are easy to break

These come from the specification and from the decision log. Violating any of them is a bug, not a style issue.

1. **`VisitSummaryDto` must never gain a diagnosis property.** It is the type used by every assistant-facing endpoint *and every SignalR payload*. The absence of the field is the security mechanism. Diagnoses travel only in `VisitDetailDto`, only from doctor-scoped endpoints. Do not merge the two types or make one inherit from the other.
2. **A doctor may only touch their own queue,** and that check lives in the **application service** — `ICurrentUser.UserId` against `Visit.DoctorId`, throwing `ForbiddenException` → 403 (D-46). Not in the controller, not in the client. `[Authorize(Policy = "DoctorOnly")]` is the coarse layer in front of it.
3. **An assistant may never register through a doctor endpoint and a doctor may never register a patient.** Both are policy-enforced.
4. **The state machine has exactly four states and exactly three transitions.** Do not add a fifth state. Deletion is a soft-delete flag, orthogonal to status.
5. **Every state transition goes through `VisitStateMachine`.** Never set `Visit.Status` directly from a service.
6. **Audit is captured in the EF Core `SaveChangesInterceptor`,** never by application services calling an audit method. If something is not being audited, fix the interceptor.
7. **Sensitive audit values are redacted for assistants** (`***` plus a `redacted: true` flag), never omitted silently.
8. **All timestamps are `DateTimeOffset` in UTC.** Formatting for display is the client's job.
9. **All primary keys are `Guid.CreateVersion7(now)`** — the `DateTimeOffset` overload. Never `Guid.NewGuid()`, and never the parameterless `CreateVersion7()`: it reads the system clock, which `Domain` may not do. Both are enforced by `BannedSymbols.txt`.
10. **`Domain` reads no ambient state.** No clock, no random source. Timestamps are parameters. Outside `Domain`, the clock is `TimeProvider` injected through DI — never `DateTimeOffset.UtcNow` inline, so tests can substitute `FakeTimeProvider`.
11. **`DomainException` means a caller broke a business rule** an aggregate can see, and becomes a 4xx. Framework guard clauses (`ArgumentNullException.ThrowIfNull`) mean *our own code has a bug* and become a 500. Application exceptions — `AuthenticationFailedException` 401, `ForbiddenException` 403, `NotFoundException` 404, `ConflictException` 409 — are for rules that need a query, an identity or a lookup, which no aggregate can decide alone. Do not collapse the three families.
12. **Value objects canonicalise before they validate.** Regex anchors are `\A` and `\z`, never `^` and `$` (`$` matches before a trailing newline). Digit classes are `[0-9]`, never `\d` (which matches every Unicode numeral). Text input is composed to NFC before character rules run.
13. **A soft-deleted `Visit` is frozen.** Every mutating method guards on it, and EF Core applies a global query filter as a second layer. Through the API the filter wins first, so a deleted visit is a **404**; the domain guard is defence in depth that the API never reaches.
14. **JWT claims use the short names `sub`, `name`, `role`, `specialtyId`.** Never `ClaimTypes.*` — those constants *are* the WS-Federation URIs. `MapInboundClaims` stays `false` and `NameClaimType` / `RoleClaimType` stay set explicitly. Under the framework default, authorization keeps working while `ICurrentUser.UserId` silently becomes null (D-37).
15. **The 500 response body is identical in every environment** — no exception type, no message, no stack trace, no `DeveloperExceptionPage`. Diagnosis is the `traceId` plus the structured log. Consequently **every query endpoint needs an integration test that actually executes its query**, because a query that only compiles fails in front of the user as an opaque 500 (D-42).
16. **Persistence interfaces speak in value objects, not the primitives inside them.** `FindByTajAsync(TajNumber)`, never `FindByTajAsync(string)`. With a value converter EF sees one column of type `TajNumber`, so `p.Taj.Digits == taj` has no translation and fails at runtime as an opaque 500 (D-47). The better reason: the method then cannot be called with a string nobody validated.
17. **Error messages disclose only what the caller can act on** (D-50). Wrong password and unknown username produce byte-identical 401 bodies. A 403 says the visit is not in your queue and never names the colleague who owns it. A 500 carries nothing. The test is: would the caller behave differently if told?
18. **The auth session never yields the token as a string** (D-55). `IAuthSession` authorises an `HttpRequestMessage`; it has no token property, so no log line, message or serialiser can reach one. P6 widens it as a named method for SignalR's query string — never as a general property.
19. **The audit tables carry no foreign keys** to `Patients`, `Users` or `Visits` (D-58). `EntityType` and `EntityId` are values. An audit entry must outlive what it describes — do not "fix" the missing relationships.
20. **Redaction is uniform, including nulls** (D-60). An assistant sees `***` where a doctor sees `null`. The shape of the data leaks even when the value does not: a preserved null would distinguish a first diagnosis from a revised one.
21. **The push payload is always `VisitSummaryDto`** — the type with no diagnosis member. No notifier method takes anything that could carry one, and recording a diagnosis publishes no event at all.
22. **A guard for "must not throw" lives at the call site, not in the contract** (D-68). The services reach `IRealtimeNotifier` through the concrete `VisitAnnouncer`, which holds the single catch. Do not move it into an implementation — a substituted one would then be unguarded.
23. **`ICurrentUser` inside `QueueHub` works because a WebSocket's lifetime sits inside its upgrade request** (D-66). Microsoft documents `IHttpContextAccessor` as unreliable in hubs. If you add an invokable hub method, re-run `A_hub_invocation_resolves_the_identity_the_same_way_a_request_does` rather than trusting it.
24. **Push callbacks are marshalled onto the UI thread in `QueueConnection`, not in the view models** (D-74). That is the boundary at which a background thread becomes the application, so one place covers every current and future subscriber. `IUiDispatcher` lives in `Client.Core`; each shell implements it in one line over `Dispatcher.UIThread`. The no-Avalonia rule in `Client.Core` does not bend for this.
25. **Where a collaborator is required for correctness, its absence is an error at construction, not a fallback at runtime** (D-75). `QueueConnection` refuses a null dispatcher. A safe default converts a wiring mistake into a behaviour, and a behaviour is far harder to notice than a crash — that is exactly how the missing marshalling hid for three phases.
26. **No `IQueryable` and no DTO on a repository interface.** Repositories and directories return domain entities; mapping to `Contracts` happens in `Application`, because the role-scoped DTOs are a security boundary (D-19, D-38). No `IRepository<T>`.

---

## Build hygiene established in P0–P3 — do not undo

- **`nuget.config` at the root clears inherited package sources.** This machine has a private Azure Artifacts feed in its user-level config; central package management fails to resolve across multiple sources. Do not remove the `<clear />`.
- **`Microsoft.OpenApi` is pinned forward to 2.7.5** to close a high-severity advisory that the transitive 2.0.0 carries. Do not relax the pin. Never suppress `NU1903`.
- **`.gitattributes` forces `eol=lf`.** The Avalonia templates emit CRLF; without this the history alternates.
- **`global.json` sets `allowPrerelease: false`.** Outside Visual Studio this defaults to true, which would silently select a preview SDK.
- **Outside Development, `/openapi/v1.json` answers 401, not 404.** The routes are not mapped, but the `FallbackPolicy` applies to requests matching no endpoint at all. That is deliberate and asserted — do not "fix" it to a 404 (D-40).
- **`app.UseHttpsRedirection()` applies outside Development only.** Under the http launch profile it logs a warning on every request, which pollutes the demo.
- **The EF Core family is pinned as a unit** (D-35). Npgsql declares a lower EF Core version than `Design` resolves, and `PrivateAssets="all"` stops the higher one flowing onward — so two projects silently compile against different versions. Do not unpin.
- **`Microsoft.EntityFrameworkCore.Design` is referenced from both Infrastructure and Api**, both `PrivateAssets="all"`. `dotnet ef` resolves the `DbContext` through the *startup* project's DI, so it needs it in Api too.
- **Database identifiers are EF's default PascalCase** (D-36). `select * from "Visits"` needs the quotes at a `psql` prompt. Do not add a naming-convention package.
- **The `xmin` concurrency token is a shadow property**, not a field on `Visit`. The generated migration declares an `xmin` column that Npgsql's SQL generator deliberately never emits — that is correct, not a bug to fix.
- **`EnforceCodeStyleInBuild` is effectively decorative** as configured: IDE analyzers default to *silent*, and raising their severity makes the stock templates fail to compile. `dotnet format` is the real gate. Do not raise IDE severities without a decision from the controller session.
- **`public partial class Program` at the end of `Program.cs`** exists so `WebApplicationFactory<Program>` can find an entry point; top-level statements generate an internal one. Do not remove it, and do not replace it with `InternalsVisibleTo` — a production assembly should not name a test assembly.
- **Avalonia XAML diagnostics are promoted in `.editorconfig`, not by `TreatWarningsAsErrors`.** The XAML compiler is an MSBuild task, so the C# setting never reaches it; the key it reads is `avalonia_xaml_diagnostic.<CODE>.severity`, **not** `dotnet_diagnostic.*` (D-61). There is no wildcard, so all 22 codes are listed explicitly. **On an Avalonia upgrade, re-check whether new codes exist** — a new code defaults back to its own severity and would slip through.
- **XAML errors hide behind incremental builds.** `dotnet build` can report success repeatedly while a broken `App.axaml` reference sits there; only a `dotnet clean` followed by a build surfaces `AVLN2000`. CI always builds clean, so CI catches it — but locally, **if a XAML change seems not to take effect, clean first**.
- **Never use `nameof(SomethingAsync)` to name a route.** MVC strips the `Async` suffix from action names by convention, so `CreatedAtAction(nameof(GetAsync), …)` names a route that does not exist and throws at runtime, *after* the transaction has committed. Use a named route constant. Outside Development this surfaces as a 409 on the client's retry rather than as the 500 it is (D-48).
- **Four endpoints carry `[AllowAnonymous]`:** `POST /api/auth/login`, `GET /health`, `/openapi/v1.json` and `/scalar`. The last two are mapped in Development only; without the attribute the reference UI loads and renders nothing (D-40).

---

## Testing

- **xUnit v2 (2.9.3) + NSubstitute + Shouldly.** Not FluentAssertions — v8+ requires a paid commercial licence. xunit.v3 was considered and declined (see `context/decisions.md` D-22); do not migrate without a controller decision.
- **Domain tests are pure**: no database, no mocks of infrastructure, no async.
- **Application tests substitute every collaborator**: no host, no database, no container. If an application test needs either, the seam is in the wrong place.
- The state machine test asserts **all sixteen ordered `(from, to)` pairs**, allowed and denied. Do not shorten it to the happy path.
- **Integration tests** use `WebApplicationFactory` with Testcontainers running real PostgreSQL. No in-memory provider — it hides provider-specific behaviour, which defeats the purpose.
- `MediQueue.Api.IntegrationTests` is the assembly that may reference `Infrastructure`, so it is also the home for tests of Infrastructure types (`JwtTokenIssuer`, the mapping) whether or not they need a host.
- **Test-only endpoints live in that test assembly** and are registered by the factory as an MVC application part (D-41). Cross-cutting concerns only — the fallback policy, the error mapping, the 500 body. **Never business logic**, and no production code may reference them.
- A new business rule without a test is not finished.
- **Mutation testing is expected in every phase report**, not optional. For each new rule, state which mutant your tests actually kill and how many fail. A test that survives its mutant is not a test — and if a mutant fails the whole suite for a structural reason rather than an assertion, say so instead of claiming the number.
- **Predict a mutant's kill count before running it, and write the prediction down first** (D-59). A prediction that matches is evidence the code is understood; a mismatch is the finding. And a rule observable on only one code path needs a test that constructs that path, or it reads as dead defensive code and someone will delete it.
- **Every mechanism a brief asserts goes into the mutant list as "remove it."** Twice now a brief and a code comment have shared a causal claim that was simply false, and both times the mutant is what said so. If removing a mechanism kills nothing, either it is inert or the tests are.
- **A test whose subject is timing, transport or concurrency must contain the mechanism that forces the condition** (D-65). Every convenient double removes it by default: a stub returning `Task.FromResult` never yields, so nothing can interleave; `TestServer` negotiates SignalR down to long polling, so a "WebSocket" test is not one. Neither is visible in the test body.
- **Where the correct behaviour is *not* doing something, pin the absence with a test** (D-72): assert that a malformed value still enables the button, count the requests that must not be made, walk an interface by reflection. And a reflection test must walk **inherited** members — `GetMethods()` on an interface omits what it extends, so checking only declared members checks half a surface while claiming to check all of it.
- **A measurement proves what it measures** (D-77). A probe showing that callbacks arrive on a different thread proves exactly that; it does not prove the framework will refuse them. Framework documentation is a reason to expect a failure, not evidence of one — say which of the two you have.
- **A composition test written without the framework cannot see the framework's constraints** (D-74). The P7 end-to-end harness was a console program with no Avalonia in it, so it could not observe a UI-thread violation no matter how much of the system it exercised. If a test is meant to prove the composed application works, it needs the parts that impose constraints.
- **A checking tool is itself checked before its output is trusted** (D-69), against at least one input known to pass and one known to fail for each mode it claims to detect. A tool that reports a failure the subject does not have is as useless as one that hides a failure it does.
- **A surviving mutant is acceptable only when the survival was predicted with its reason** (D-64). Otherwise it is a hole, not defence in depth.
- **A mutant the compiler rejects is a structural kill, not a test count** (D-53). Deleting the last use of an injected dependency is a build error under warnings-as-errors, so some mutants cannot be written at all. Say so, then write the nearest variant that does compile and report *that* number.
- **A negative test must be shown to fail for the stated reason, not merely to fail** (D-45). Choose the endpoint or fixture with the fewest other preconditions, so the asserted status can only come from the rule under test. Two P3 expiry tests asserted 401 and were green while asserting nothing, because the 401 came from "user not found" rather than from the expiry.
- **A test that needs reflection to build a fixture is reporting a modelling gap.** Add the behaviour to the domain rather than reaching around it.
- Persistence tests share one container. Tests needing an empty schema get their own migrated database; the rest isolate with unique TAJs and usernames. Note that a GUID suffix does not work as an isolation token — `PatientName` rejects digits; unique names map GUID characters into the letter range and TAJ numbers come from a counter. **Those generated TAJ numbers are well-formed but not necessarily checksum-valid**, so any test that switches `Validation:TajChecksumEnabled` on must generate valid ones itself.
- Lifecycle tests **create their own data through the API** rather than mutating seeded rows, so no test depends on seed state or on another test's order.

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
- **Every commit must build, pass its tests, and start** (D-63). A build-only check is not enough: composition is not a compile-time property, and the second defective commit in this history compiles perfectly and cannot boot, because a seam landed one commit ahead of its registered implementation.
  - **A seam and its implementation belong in one commit.** An interface with nothing registered against it is not a working system even though it is a compiling one.
  - A project is added to `MediQueue.sln` in the *same* commit that creates its `.csproj`. Never stage solution entries ahead of the projects they reference — that is how the *first* defective commit happened.
  - **Verify before you push, not after.** Run `scripts/verify-history.sh` over the phase's commit range. Two phases claimed per-commit verification while actually running it after the push, and found nothing by luck; the third did not get lucky.

---

## Context files

The controller session maintains these in `context/` (gitignored — they are not part of the submission):

| File | Contents |
|---|---|
| `context/handover.md` | Founding context: full task specification, constraints, company and evaluation context |
| `context/plan.md` | Stack, architecture, data model, API surface, phase breakdown |
| `context/decisions.md` | Decision log with rationale — **authoritative** over `plan.md` |
| `context/progress.md` | Phase status board, resolved versions, open risks |
| `context/diary.md` | Session log |
| `context/pre-defence-checklist.md` | What must be verified before 2026-08-11 |
| `context/demo-script-candidates.md` | Accumulating material for `docs/demo-script.md` |

Read `context/plan.md` and `context/decisions.md` before starting any phase. If a phase brief contradicts them, say so rather than guessing.

---

## Working model

- Work proceeds in **phases**. Each phase arrives as a self-contained brief with a goal, scope, file list, and acceptance criteria.
- **Do only what the brief asks.** If something outside the scope looks broken or missing, note it in the report instead of fixing it — the controller session decides what happens next.
- Briefs carry a **"Verification Attila performs"** section for anything needing human eyes. That section is *not* part of your acceptance criteria and you are not expected to satisfy it.
- **Report at the end of every phase**: what was built, what deviated from the brief and why, what is broken, what the next phase needs to know.
- **Every excerpt in a report states the command that produced it** (D-56), especially when that command is not the default one. An unlabelled console excerpt once produced a fictional risk item, a checklist entry and the first item of the next brief.
- **Every CI claim cites the run for the head commit.** GitHub has silently dropped a push trigger **twice** in this project; "CI is green" is only true if a run exists for the commit you pushed. `workflow_dispatch` is enabled so a missing run can be started by hand. Note also that GitHub reports a **cancelled** run's conclusion as `failure` — check `gh run view` before calling a red entry a failure.
- **The repository is read as it is, not as it will be** (D-62). It is public and it is the submission. If a document in it is false today, it is false to an evaluator today — "P9 will rewrite it" is not a defence for the README's front page.
- If a brief turns out to be wrong or impossible, stop and say so. Do not improvise around it. This has happened in every phase so far and the reports were right each time.
