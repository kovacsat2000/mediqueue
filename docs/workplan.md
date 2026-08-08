# Work plan — MediQueue

**Scenario.** I am the Tech Lead. I have **two junior developers**, both
competent in C# and neither having built a system of this shape before. We are
building MediQueue from nothing.

This is a plan to execute, not a description of what was built. Where the
estimates reflect something this build actually learned, the note says so —
those are the numbers I would defend hardest, because they are measured rather
than guessed.

**Team and calendar.** Three people. I am **50% hands-on** — the rest is review,
unblocking and the design work below. Estimates are in **developer-days** and
assume interruptions; a "day" is roughly six focused hours.

**Total: ~13 working days elapsed**, about 28 developer-days of effort.

---

## 1. The shape of the plan

The system has one property that dictates the sequencing: **three requirements
are silent when they are wrong.**

- Role-based access — a leak produces no error, just a field that should not
  have been there.
- The audit trail — a missing entry looks exactly like nothing having happened.
- Identity — an actor recorded as null still leaves every authorization test
  passing.

Everything else fails loudly: a broken state transition throws, a broken query
500s, a broken screen is visibly broken. **So the plan front-loads the silent
things onto me, and gives the juniors the loud ones**, which is also where they
learn fastest because the feedback is immediate.

**The criterion is about the task, not the area** — and getting that distinction
wrong is how a lead ends up hoarding work. The question is not *"can this part
of the system fail silently"* but **"does the task, as briefed, make the failure
loud?"** Those give different answers, and the second one is the useful one.

Value objects (B3) are the example. Validation on a national identifier fails
silently, so by the first reading it is lead work. But a junior handed *"write
`TajNumber`"* fails silently, whereas a junior handed the three specific traps —
`\A`/`\z` instead of `^`/`$`, `[0-9]` instead of `\d`, NFC before the character
rules — and asked to prove each one with a test **cannot**: the briefing has
converted an invisible failure into a visible, learnable one. The task changed
category because of how it was written down.

That is also the honest limit of the split. It works while I can write a
briefing that specific, which means it works where I already know the traps. For
the audit interceptor I cannot — the judgement it needs (what to do when the
actor is unknown) is not a list of gotchas but a decision about what the system
owes its reader — and that is why it stays with me. **If somebody wants to argue
a task across the line, the argument to make is that the briefing can carry
it.**

The second property is that the desktop clients cannot be built until there is
something to talk to, and the push channel cannot be demonstrated until there
are two clients. That makes the server the critical path and the clients the
thing most at risk of being rushed at the end — so a throwaway client goes in
early, deliberately, at the point where it is cheapest.

---

## 2. Work breakdown

Tasks are sized so a junior can pick one up and finish it inside a day or two.
**Owner** is `L` (lead), `J` (either junior), `J1`/`J2` where it matters that
they are different people.

### Phase A — Foundations (2 days elapsed)

| # | Task | Owner | Est. | Depends on |
|---|---|---|---|---|
| A1 | Solution skeleton, seven projects, dependency rule enforced by project references only | **L** | 0.5 | — |
| A2 | Central package management, warnings-as-errors, `.editorconfig`, `dotnet format` | J1 | 0.5 | A1 |
| A3 | `docker-compose.yml` for PostgreSQL; connection string in configuration | J2 | 0.5 | A1 |
| A4 | CI: restore, build, test, format-check on every push | J1 | 0.5 | A2 |
| A5 | Banned-API analyzer on `Domain` — no clock, no `Guid.NewGuid` | **L** | 0.5 | A1 |
| A6 | Serilog, health check, problem-details error handler | J2 | 1 | A1 |

**A1 and A5 are mine** and they are half a day between them. The project
reference graph *is* the architecture — if `Application` gains a reference to
`Infrastructure` in week one, nothing later will remove it — and the analyzer is
what turns "the domain is pure" from a code-review opinion into a build failure.
Both are cheap to do right and expensive to retrofit.

**Parallel:** A2/A4 (J1) and A3/A6 (J2) run alongside each other.

### Phase B — Domain and persistence (3 days elapsed)

| # | Task | Owner | Est. | Depends on |
|---|---|---|---|---|
| B1 | **Data model and aggregate boundaries** — `Patient` vs `Visit`, and why | **L** | 1 | A1 |
| B2 | **The state machine** — four states, transition table, exhaustive test | **L** | 1 | B1 |
| B3 | Value objects: `TajNumber`, `PatientName`, canonicalisation and validation | J1 | 1.5 | B1 |
| B4 | `Specialty`, `User`, the role invariant | J2 | 1 | B1 |
| B5 | EF Core configurations, value converters, first migration | J2 | 1.5 | B3, B4 |
| B6 | Seed data — a plausible morning, including one deactivated doctor | J1 | 0.5 | B5 |
| B7 | Testcontainers harness: real PostgreSQL, one container per run | **L** | 1 | B5 |

**B1 and B2 are mine.** The aggregate boundary is the decision the rest of the
system cannot recover from — get `Patient`/`Visit` wrong and either a returning
patient overwrites their own history or the audit trail cannot tell "came back"
from "was edited". The state machine is small enough to look junior-sized and is
not: the value is in the exhaustive transition table and in the error carrying
`allowedTransitions` as data, and both are easy to reduce to "a few ifs" by
someone who has not been told why.

**B3 is a junior task with a specific briefing**, and this is the most important
briefing in the plan. Tell them: **regex anchors are `\A` and `\z`, never `^`
and `$`; digit classes are `[0-9]`, never `\d`; and text is composed to NFC
before character rules run.** Then have them write the tests that prove each
one. This build shipped `$` and `\d` first and both are invisible in review —
`$` matches before a trailing newline, and `\d` accepts every Unicode numeral,
so `"123-456-788\n"` and Devanagari digits were both valid social-security
numbers. Half a day of the 1.5 is that briefing and those tests.

**B6 has a hidden requirement** worth stating in the ticket: seed one doctor
**deactivated**, alone in a specialty. Without it, "no doctor available" is
unreachable and two rules become untestable — and nobody discovers that until
they try to write the test.

### Phase C — API and authorization (2.5 days elapsed)

| # | Task | Owner | Est. | Depends on |
|---|---|---|---|---|
| C1 | **JWT issuance and validation, claim naming** | **L** | 1 | B4 |
| C2 | **Role policies, fallback policy, the ownership rule** | **L** | 1 | C1 |
| C3 | **Role-scoped DTOs — the summary type with no diagnosis member** | **L** | 0.5 | B1 |
| C4 | Visit lifecycle endpoints, mapped to the state machine | J1 | 2 | B2, C2 |
| C5 | Queue and directory read endpoints | J2 | 1.5 | B5, C2 |
| C6 | Doctor assignment strategy behind an interface | J2 | 1 | C5 |
| C7 | Integration tests for every endpoint, against the real container | J1+J2 | 2 | C4, C5 |

**C1, C2 and C3 are mine, and they are the reason this phase is not
parallelisable at the front.** All three are silent-failure work:

- **C1** carries the claim-mapping trap. The framework's default inbound mapping
  rewrites `sub` to a WS-Federation URI, so `ICurrentUser.UserId` returns null
  while `IsInRole` keeps working. Every authorization test passes and every
  audit entry gets a null actor. **Budget a day and hand the team the answer up
  front** — see §6.
- **C2** is where "a doctor may only touch their own queue" lives. It belongs in
  the application service, not the controller, so that a new endpoint cannot
  forget it and so it is unit-testable with everything substituted.
- **C3** is fifteen minutes of typing and the most consequential fifteen minutes
  in the project. Two DTOs, duplicated on purpose, one of which *cannot* carry a
  diagnosis.

**C7 is the pairing task.** Both juniors, on the endpoints the other one wrote.
It is the cheapest review mechanism available and it spreads knowledge of the
whole surface.

### Phase D — Audit (2 days elapsed)

| # | Task | Owner | Est. | Depends on |
|---|---|---|---|---|
| D1 | **The `SaveChanges` interceptor** — actor, action, per-field changes | **L** | 1.5 | C1, B5 |
| D2 | **Sensitivity attribute and the redaction rule** | **L** | 0.5 | D1 |
| D3 | Audit entities, migration, indexes for the two required filters | J1 | 1 | B5 |
| D4 | `GET /api/audit` with filters, paging and clamping | J2 | 1 | D3 |
| D5 | Integration tests including raw-JSON redaction assertions | J1 | 1 | D1, D4 |

**D1 and D2 are mine, and this is the least negotiable split in the plan.** The
interceptor is the one place where "did we record it" is decided, and its
failure mode is an empty log that looks like a quiet day. It also contains a
judgement a junior should not have to make alone: **what to do when the actor is
unknown**. The tempting answer — skip the entry — turns a broken identity
pipeline into a silently empty audit trail, which is worse than a silently
anonymous one. Write the entry, log a warning, and make suppression an explicit
opt-out with one caller.

**D3, D4 and D5 are properly junior.** The schema, the query and the tests are
all loud: an index that is missing shows up as a slow query, a clamp that is
wrong shows up in a test, a filter that is wrong returns the wrong rows.

**D5 needs one line in the ticket:** assert on the **raw JSON**, not on a
deserialised object. Deserialising into the DTO discards the field that leaked
and passes against a broken server. This is not obvious and it is the difference
between a test and a formality.

### Phase E — Push, and a throwaway client (2 days elapsed)

| # | Task | Owner | Est. | Depends on |
|---|---|---|---|---|
| E1 | **Hub, groups, and the routing table** | **L** | 1 | C2 |
| E2 | **`access_token` on the hub path and nowhere else** | **L** | 0.5 | E1 |
| E3 | Notifier interface and its call sites after each commit | J1 | 1 | E1, C4 |
| E4 | **Walking-skeleton client: sign in, show a queue, nothing else** | J2 | 1.5 | C5 |
| E5 | Hub integration tests over a real WebSocket | J1 | 1 | E1 |

**E1 and E2 are mine because group membership *is* the authorization.** A doctor
is placed in their own group and no other, so another doctor's event cannot
reach them — there is no filtering step to get wrong, which is the point. E2 is
five lines and one of them is a security boundary: a token accepted from the
query string on *every* path means every access log holds a live credential.
Restrict it to the hub path and say why in a comment.

**E4 is the item most likely to be cut and should not be.** A deliberately
throwaway client, built before the real ones, at the point where a composition
bug is cheapest to find. In this build it found one immediately: **320 tests
green while the queue listed every patient twice**, because a fire-and-forget
event handler raced the refresh. Every view model was individually correct.
Unit tests verify components; they cannot see composition.

Give this to a junior with the explicit framing that it will be thrown away.
Otherwise they will polish it.

### Phase F — The clients (3 days elapsed)

| # | Task | Owner | Est. | Depends on |
|---|---|---|---|---|
| F1 | **`Client.Core` boundaries: role-split API, no UI framework reference** | **L** | 0.5 | C3 |
| F2 | **UI-thread marshalling abstraction** | **L** | 0.5 | E4 |
| F3 | Doctor client: queue, actions, detail pane | J1 | 2.5 | F1, E4 |
| F4 | Assistant client: registration, unrouted list, all queues | J2 | 3 | F1, E4 |
| F5 | View-model tests for both, no UI framework | J1+J2 | 2 | F3, F4 |
| F6 | End-to-end drive of the real view models against a running server | **L** | 0.5 | F3, F4 |

**F1 and F2 are mine and both are small.** F1 splits the client API into
`IAssistantApi` and `IDoctorApi` over one implementation, so the assistant
application has no expressible way to request a diagnosis — the same guarantee
as C3, one layer out. F2 is the abstraction that keeps `Client.Core` free of a
UI framework while still marshalling push callbacks onto the UI thread; without
it the clients work in every test and fail intermittently in front of a user.

**F4 is bigger than F3** and the ticket should say why: the assistant has
several lists and a visit must be in **exactly one** of them at every instant,
so a routing event is a remove-then-insert under one lock rather than two
independent updates.

**F6 is mine and takes half a day.** Drive the real view models against a real
server before calling the phase done. It is the only thing that sees
composition.

### Phase G — Deliverables (1.5 days elapsed)

| # | Task | Owner | Est. | Depends on |
|---|---|---|---|---|
| G1 | README: run it, what is done, trade-offs, what would be done differently | **L** | 1 | F |
| G2 | This work plan | **L** | 0.5 | — |
| G3 | Decision log, curated | **L** | 0.5 | — |
| G4 | End-to-end smoke script, standard library only | J1 | 0.5 | F |
| G5 | Demo script, walked once with a clock | **L** + J | 0.5 | F |

The documents are mine because they are the argument, and the argument is the
lead's job. **G4 is a good junior task** and a genuinely useful one: it is the
first thing anybody runs on the morning of a demonstration.

---

## 3. Sequencing, parallelism and the critical path

```
A1 ─┬─ A2 ─ A4                (J1)
    ├─ A3 ─ A6                (J2)
    └─ A5                     (L)
        │
    B1 ─┼─ B2                 (L)   ← critical path starts here
        ├─ B3                 (J1)
        └─ B4 ─ B5 ─ B6       (J2)
                │
            B7  │             (L)
                │
    C1 ─ C2 ─┬─ C4            (J1)
      (L)    ├─ C5 ─ C6       (J2)
             └─ C3 (L)
                │
             C7               (J1+J2, paired)
                │
    D1 ─ D2 (L) ─┬─ D3 ─ D4   (J)
                 └─ D5        (J1)
                │
    E1 ─ E2 (L) ─┬─ E3, E5    (J1)
                 └─ E4        (J2)
                │
    F1, F2 (L) ─┬─ F3         (J1)
                └─ F4         (J2)
                │
             F5, F6
                │
             G1…G5
```

**The critical path is B1 → B2 → C1 → C2 → D1 → E1 → F3/F4.** It is almost
entirely mine, which is the honest consequence of putting the silent-failure
work with the lead — and it is the plan's main risk. §6 says what I do about it.

**What genuinely parallelises.** Phase A splits cleanly. In B, value objects
(J1) and the user/specialty model (J2) are independent once B1 lands. In C, the
lifecycle endpoints and the read endpoints touch different controllers. In F,
the two clients are separate applications over a shared core that is already
frozen by F1.

**What does not.** Nothing in C starts before C1/C2, because every endpoint
needs the policies. Nothing in D starts before the schema. F3 and F4 both need
F1 finished, not in progress — a shared project being edited under two people is
how a shared project becomes a merge conflict.

### Week one versus week three

**Week one** (Phases A, B, most of C) is the one that feels slow and is not.
Nothing is demonstrable until the middle of week two. The visible output at the
end of week one is a domain model, a migration, a database full of plausible
Hungarian names, and a test suite that runs against a real container in a CI
pipeline. **If the plan is going to be challenged, it will be challenged here**,
and the answer is that every foundation item is one that cannot be retrofitted:
the dependency graph, the analyzer, the aggregate boundary, the transition
table.

**Week two** is where it becomes a system — endpoints, the audit trail, the hub,
and the throwaway client that makes the push visible for the first time. This is
the week the juniors' throughput doubles, because the shape is established and
the tasks stop needing a design conversation first.

**Week three** is the clients and the deliverables, and it is the week most
likely to overrun. The mitigation is E4: the throwaway client has already found
the composition bugs, so F3 and F4 are building on a path that has been walked.

---

## 4. Onboarding: what the juniors need on day one

Not "read the codebase" — there is no codebase. Concretely:

**Before they write anything (half a day, together, with me).**

1. **The five specification requirements, and which two are silent when wrong.**
   They should be able to say which parts of the system will not tell them they
   have broken it. This is the single most useful thing they can carry.
2. **The dependency rule, in one diagram.** `Domain` and `Contracts` reference
   nothing; nothing points inward from infrastructure. And what to do when they
   want to break it: introduce an interface in the inner layer, implement it in
   the outer one, and tell me — because that is a design conversation, not a
   pull request.
3. **The two DTOs, and why they are duplicated.** If they leave the room
   thinking that duplication is an accident, they will "fix" it.

**Environment, working the first morning.** A single `docker compose up -d db`,
a `dotnet run` that migrates and seeds, and a seeded practice they can log into.
Nobody's first day should be spent on a connection string.

**A definition of done pinned somewhere they will see it** (§5), and a first
task that is deliberately small and complete — A2 and A3 are both good: real,
useful, unblocking, and impossible to get badly wrong.

**Three named traps, given as answers rather than as discoveries.** The claim
mapping, the regex anchors, and raw-JSON assertions. Each costs a day to find
and a sentence to prevent. There is no pedagogy in letting somebody rediscover
that `\d` matches Devanagari.

**A weekly half-hour** where each junior explains one decision they made to the
other. It is the cheapest way to spread context and it surfaces
misunderstandings while they are still cheap.

---

## 5. Definition of done, review policy and CI gates

### Done

A task is done when:

- The behaviour is covered by a test at the right level — domain rules by unit
  tests with no database; endpoint behaviour by an integration test against the
  real container.
- **Every query endpoint has a test that actually executes its query.** A query
  that only compiles fails in front of a user as an opaque 500. This one is
  non-negotiable and it is on the checklist because it was learned the hard way.
- `dotnet build` is clean — warnings are errors — and `dotnet format
  --verify-no-changes` passes.
- The commit **restores, builds, passes and starts**. Not "builds": a commit can
  compile perfectly and fail to start because a dependency was registered with
  no implementation, and composition is not a compile-time property.
- Anything non-obvious has a comment saying *why*, not *what*.

### Review: not negotiable

- **A change to an authorization boundary, the audit interceptor, the state
  machine or the data model comes to me.** Not because juniors cannot write
  them, but because a mistake there is silent.
- **No new dependency without a conversation.** Adding a package is a decision
  with a maintenance tail.
- **A new business rule arrives with a test**, and the test must be shown to
  fail without the rule. A negative test that passes for the wrong reason is
  worse than none, because it is counted as coverage.
- **No `IQueryable` or DTO on a repository interface.** The role-scoped
  projections are a security boundary and belong in one reviewable layer.
- **Nothing is logged that a user could not be shown.** No tokens, no
  diagnoses, no passwords.

### Review: taste, and I will say so

Naming, file organisation, whether a helper is worth extracting, LINQ versus a
loop. I have opinions and I will offer them as opinions. A junior who
consistently makes a defensible call I would not have made is doing well, and
being told so matters more than the call.

### CI gates

| Gate | Blocks merge |
|---|---|
| `dotnet build` with warnings as errors | yes |
| `dotnet test` — all projects, real database container | yes |
| `dotnet format --verify-no-changes` | yes |
| `dotnet list package --vulnerable` | yes, on high severity |
| Per-commit verification before push | yes, by script |

**Mutation testing is a gate, not a nicety**, on the three silent requirements.
Before any of them is called done, one thing is changed — remove the redaction,
remove the ownership check, remove the audit call — and the number of failing
tests is recorded **against a prediction made first**. If removing a rule kills
nothing, either the rule is inert or the tests are, and both are worth knowing.

This is the gate I would have to argue for hardest, so: in this build it found
two tests that were green and asserting nothing, two concurrency tests that
could not observe the condition they were named for, and two documented causal
claims that were simply false. Coverage would have reported all of them as
covered.

---

## 6. Risks, and how the plan absorbs them

| Risk | Likelihood | Impact | What the plan does |
|---|---|---|---|
| **The claim-mapping trap** | Certain if nobody knows | ~1 day, and a silently null audit actor | Told to the team on day one as an answer, not left to be found. C1 is lead work and budgeted at a full day for five lines of configuration |
| **Validation defects invisible to review** | High | A malformed TAJ accepted in production | B3's briefing names the three specific mistakes; mutation testing on the value objects is a gate |
| **Silent authorization leak** | Medium | The most serious defect the system can have | Structural, not procedural: the assistant's type has no diagnosis member and the assistant's client interface cannot request one. Both asserted by tests, one by reflection |
| **The critical path is mostly the lead** | Certain | Everything stalls if I am pulled away | The juniors always have an unblocked queue: tests, seed data, read endpoints, the smoke script. C7 and F5 are deliberately parked as work that can absorb a week |
| **Client composition bugs found late** | High without E4 | A rushed final week | E4 exists for this. It costs 1.5 days and found a duplicate-row race that 320 passing tests did not |
| **Cross-thread UI failure from push** | High, and intermittent | Fails in a demonstration, not in tests | F2. Push callbacks arrive on a thread-pool thread and the tests have no UI thread to notice, so it must be designed rather than discovered |
| **Juniors blocked on the shared client core** | Medium | Two people editing one project | F1 is finished and frozen before F3/F4 start |
| **Scope creep into the state machine** | Medium | Endless "what about no-shows" | Four states and three transitions is what the specification describes. Anything else goes on a list and is discussed, not built |
| **Estimates optimistic on the clients** | Medium | Week three overruns | F3 and F4 are the two largest single tasks in the plan and are the first thing I would cut scope from — the assignment requires *one* working client, so the doctor client is the commitment and the assistant client is the stretch |

### If we are running late

Cut in this order, and say so out loud rather than quietly:

1. The assistant client's polish — the list works, the styling waits.
2. The second desktop client entirely. The assignment requires one.
3. The audit query's date-range filter; patient and user are the two the
   specification names.
4. **Never** the audit interceptor, the role-scoped DTOs, or the ownership
   check. Those are the assignment.

---

## 7. What I would tell the team on the last day

The parts of this system worth being proud of are the ones where a mistake is
impossible rather than merely discouraged — the type that cannot carry a
diagnosis, the interceptor a use case cannot forget, the analyzer that fails the
build when the domain reads a clock.

The parts worth being honest about are the one guarantee that is a runtime
branch because no type could express it, and the two commits in the history that
do not build. Both are in the README, because a system's documentation should be
the place a reviewer *stops* finding surprises.
