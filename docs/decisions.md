# Decisions

The choices in MediQueue that were not obvious, with what they cost.

This is a curated subset of a longer working log. Each entry states the
decision, the alternatives that were seriously considered, and the trade-off
that was accepted — **an entry without a trade-off is marketing**, and the
trade-offs are the half worth reading.

---

## 1. C# on .NET 10, and not a backend-as-a-service

**Decision.** A hand-written ASP.NET Core server on .NET 10 (LTS), with EF Core
over PostgreSQL and SignalR for push.

**Alternatives.** Supabase or a comparable BaaS would have supplied
authentication, a database, row-level security and a real-time channel in an
afternoon. Node with Nest, or Go, would have been faster to stand up than
ASP.NET if the stack were free.

**Why not.** The exercise is designing a server. Buying one answers a different
question — and the audit requirement in particular wants server-side
interception of every write, which is precisely the part a BaaS takes away.
Row-level security could enforce *who may read a row*; it could not have
produced a field-level audit trail with per-field redaction without writing the
same logic somewhere less testable.

**Trade-off admitted.** Considerably more code than the shortest path to a
working demo, and every piece of it is something to defend. A BaaS would also
have had a better answer for hosting.

---

## 2. Avalonia UI, not WPF — and no web front end

**Decision.** Two Avalonia 12 desktop applications sharing a `Client.Core`
project that has no UI framework reference at all.

**Alternatives.** WPF is the conventional answer for a .NET desktop client and
better known to most reviewers. MAUI is the current first-party
cross-platform story. A web front end would have been faster than either.

**Why not.** WPF is Windows-only and this was developed on macOS, so "it builds
on my machine" would have been untestable for the whole project. MAUI's desktop
targets are weaker for a multi-window, list-heavy application. A web front end
is excluded by the assignment, which asks for desktop clients — and that
constraint is what makes SignalR's desktop client relevant rather than
incidental.

**Trade-off admitted.** Avalonia is less familiar to a reviewer than WPF, its
XAML dialect differs in small ways, and its tooling is thinner. It also cost a
real day: XAML diagnostics are *not* covered by `TreatWarningsAsErrors`, and a
deprecation shipped as a green build until the mechanism was found.

---

## 3. `Patient` and `Visit` are separate aggregates

**Decision.** A patient is a person; a visit is one episode of care. Registering
a returning patient reuses their record and creates a second visit.

**Alternative.** The specification's wording reads as one entity — a patient who
*has* a status. That is the literal reading and it is simpler.

**Why not.** "Done" is not a permanent property of a human being. With one
entity, a returning patient either overwrites their own history or becomes a
second person, and the audit trail cannot distinguish "this patient came back"
from "somebody edited this record". The TAJ number is the natural key of a
person; the state machine describes an episode.

**Trade-off admitted.** A `Visit` holds a `PatientId` and no navigation
property, so nothing can be `Include`d across the boundary. Patient names for
the queue screens come from a batched lookup assembled in application code —
a manual join, paid deliberately, and the first thing a read model would
replace.

---

## 4. The assistant's DTO has no diagnosis member

**Decision.** Two projections. `VisitSummaryDto` **declares no diagnosis
property at all**; `VisitDetailDto` has one. Every assistant-facing endpoint and
every push payload uses the summary type. The two are duplicated rather than
related by inheritance.

**Alternatives.** One DTO with the field nulled out for assistants. Or
inheritance, with the detail type extending the summary.

**Why not.** "Remember to strip the diagnosis" is a rule that can be forgotten
in a new endpoint, a new event or a new query. A type that cannot carry the
field cannot leak it — the policy becomes a compile-time guarantee. Inheritance
would reintroduce the risk from the other direction: a member added to the base
would silently appear on the derived type, and the arrangement invites someone
to try it the wrong way round.

The same idea was later extended to the client: `IAssistantApi` declares no
member that returns the detail type, so the assistant *application* has no
expressible way to ask for a diagnosis.

**Trade-off admitted.** Fifteen properties are written out twice, and a field
added to a visit must be added in two places. That duplication is the mechanism,
not an oversight — but it is duplication, and a reviewer is right to notice it.

**How it is checked.** An integration test asserts the raw JSON body contains no
`diagnosis` key — *absent*, not null — because deserialising into the DTO would
have discarded the field in silence and passed against a leaking server.

---

## 5. Audit sensitivity is per field, not per entry

**Decision.** `AuditEntry` records who, when, which record and what action;
`AuditFieldChange` records one field's movement and carries an `IsSensitive`
flag, taken from a `[SensitiveAudit]` attribute on the domain property. When an
assistant reads the log, sensitive values become `***` and the DTO carries
`redacted: true`.

**Alternatives.** Mark whole entries sensitive. Or store the changes as a JSON
blob rather than as rows.

**Why not.** A visit update can touch a diagnosis and a status in one action.
Marking the entry would hide the ordinary field along with the clinical one, and
an assistant is entitled to see that a visit changed and who changed it.
Normalised rows rather than a blob keep "filter by patient or by user" as plain
indexed SQL and keep redaction a per-row decision.

**The requirement this exists to resolve.** The assignment asks for a queryable
audit log *and* forbids assistants from seeing diagnoses. Together those open a
back door: a naive audit log hands over at the back what the API withholds at
the front.

**Trade-off admitted.** This is **the one guarantee in the system enforced by a
runtime branch rather than by a type**, because the same field must be visible
to a doctor and hidden from an assistant — no shape of DTO can decide that. It
is therefore written in exactly one method, pinned by tests that read raw JSON
on every page of a paged response, and fails closed: the rule is "only a doctor
may", so an unrecognised role is redacted.

---

## 6. Auditing is an EF Core interceptor, not a call in each use case

**Decision.** A `SaveChangesInterceptor` walks the change tracker before every
write and produces the entries, inside the same transaction as the change they
describe. Every entity type is audited **except the two audit types**, excluded
by type rather than by a maintained list.

**Alternative.** An explicit `audit.Record(...)` call in each use case, which is
more visible and easier to read.

**Why not.** A use case can forget, and this one must not. A new use case is
audited because it saves; a new entity is audited because it is not one of the
two exclusions. Both are properties of the mechanism rather than of anyone's
memory.

**Trade-off admitted.** Auditing becomes invisible at the call site — a reader
of `VisitLifecycleService` sees nothing about it — and debugging goes through an
interceptor most reviewers will not expect. It also means the audit shape is
coupled to EF's change tracking rather than to the domain's own vocabulary.

**One deliberate risk accepted.** If the acting user cannot be determined, the
entry is written anyway with a null actor **and a warning is logged** — never
skipped. Suppression is a separate, explicit opt-out with exactly one caller,
the seeder, whose rows are fixture rather than history. "No user, no entry"
would have turned a broken identity pipeline into an audit log that was silently
*empty* rather than silently anonymous, which is strictly worse.

---

## 7. Value objects, and what a loose regex cost

**Decision.** `TajNumber` and `PatientName` are value objects that canonicalise
before they validate. Persistence interfaces speak in the value object —
`FindByTajAsync(TajNumber)`, never `FindByTajAsync(string)`.

**Alternatives.** Validate strings at the API boundary and store primitives.

**Why not.** The interface then cannot be called with a string nobody validated.
The parse happens at the trust boundary, the type carries the proof inward, and
"look this patient up by whatever the client sent" stops being expressible.

**What it cost, and why it is in this document.** The first implementation used
`^` and `$` as regex anchors and `\d` as the digit class. Both are wrong here
and both are invisible in review:

- `$` matches **before a trailing newline**, so `"123-456-788\n"` was accepted.
- `\d` matches **every Unicode decimal digit**, so Arabic-Indic and Devanagari
  numerals were accepted as a Hungarian social-security number.

The anchors are now `\A` and `\z`, the digit class is `[0-9]`, and text is
composed to NFC before character rules run. None of that was found by reading;
it was found by testing the inputs a regex is wrong about.

**Trade-off admitted.** More types, more converters, and EF Core needs a value
converter for each — which in turn means a query written against the primitive
inside compiles and then fails at run time as an opaque 500. That trap is real
and it is the reason the interfaces take the value object.

---

## 8. The state machine is a table, and the test asserts all sixteen pairs

**Decision.** Four states, three legal transitions, expressed as an explicit
transition table. Every mutating method on `Visit` asks the state machine for
permission first; nothing else may write `Status`.

**Alternative.** `if` statements in each method, which is what the four states
would naturally attract.

**Why not.** The table is one thing to read and one thing to change, and it
makes the illegal transitions as visible as the legal ones. The invalid-move
error can then carry `allowedTransitions` as data, so a client renders "this
patient has already been released" without parsing prose.

**The test is the point.** It asserts **all sixteen ordered `(from, to)`
pairs** — the three that are allowed and the thirteen that are not. A happy-path
test would have passed against a state machine that allowed everything.

**Trade-off admitted.** Slightly more ceremony than four states strictly need,
and a fifth state would mean touching the table, the enum and the test rather
than one method. That is the intended cost: it makes adding a state a deliberate
act.

**Deletion is not a fifth state.** It is a flag, orthogonal to status, so a
withdrawn visit still remembers how far it had progressed — which is what makes
its audit history worth reading.

---

## 9. Identity uses short claim names, and the framework default is a trap

**Decision.** Tokens carry `sub`, `name`, `role` and `specialtyId`.
`MapInboundClaims` is off and `NameClaimType`/`RoleClaimType` are set
explicitly.

**Alternative.** Leave the framework's default inbound mapping on, which is what
every tutorial does.

**Why it matters more than it looks.** Under the default, the handler rewrites
incoming claims to WS-Federation URIs. Measured on a real request:

| | default mapping | mapping disabled |
|---|---|---|
| `IsInRole("Doctor")` | **true** | true |
| `ICurrentUser.UserId` | **null** | the user id |

**Authorization keeps working.** The handler rewrites `role` to the URI the
framework's default `RoleClaimType` already expects, so the two cancel out and
every authorization test passes. Meanwhile `sub` becomes `nameidentifier`, the
current user's id silently becomes null, and **every audit entry would have been
written with no actor** — in a system whose specification requires a record of
*who* changed what.

Reverting to the defaults fails exactly two tests, and **neither is an
authorization test**. A suite that checked only roles would have shipped this.

**Trade-off admitted.** Short names are RFC 7519 registered names but are not
what .NET tooling assumes, so any library reading `ClaimTypes.NameIdentifier`
off this principal finds nothing. That is a real integration cost, accepted
because the standard should win over the framework default.

---

## 10. Doctor assignment is an injectable strategy

**Decision.** The assistant chooses a *specialty*; the server chooses the
doctor, through `IDoctorAssignmentStrategy`. The default implementation picks
the shortest queue, with deterministic tie-breaking.

**Alternative.** Let the assistant pick the doctor, which is one less moving
part and what a receptionist might expect.

**Why not.** The specification puts selection on the system. It is also the only
genuinely algorithmic rule in the assignment, so naming it as a seam is what
makes "load balancing by a different policy" a registration change rather than
an edit.

**Trade-off admitted.** The interface takes a `specialtyId` the default
implementation never reads, because the caller has already filtered the
candidates. An unused parameter is a smell unless the reason is written down —
it is there so that a specialty-aware policy is a swap and not an interface
change, and that reason lives in the XML documentation on the interface.

The clients therefore cannot show *which* doctors are free before routing, and
"no doctor is currently available in Reumatológia" arrives only after the
attempt. Showing availability would invite the assistant to pick a doctor, which
is the decision the specification took away from them, so the refusal message is
the interface.

---

## 11. Error messages disclose only what the caller can act on

**Decision.** One policy, applied everywhere.

| Where | What is withheld |
|---|---|
| 401 on sign-in | Whether the username exists. Wrong password and unknown user produce **byte-identical** bodies once the trace id is removed — asserted. |
| 403 on another doctor's visit | Whose visit it is. The message never names the colleague. |
| 500 | The exception type, message and stack trace — **in every environment**. |

**Alternative.** A richer 500 body in Development, which is what
`DeveloperExceptionPage` is for.

**Why not.** Two error models means the one that is tested is not the one that
ships. One model, one test, one mutant.

**The test for the policy.** Would the caller behave differently if told? A
doctor who learns the visit belongs to Dr. Nagy can do nothing with that. An
attacker who learns a username exists can do a great deal.

**Trade-off admitted, and paid.** Support diagnosis is harder by exactly this
much, which is what the trace id on every body is for. It was paid during
development too: a LINQ join that did not translate arrived as an opaque 500 and
the diagnosis was a log-reading exercise. That is the design working, and it is
the argument for the rule that every query endpoint needs an integration test
that actually executes its query.

---

## 12. Mutation testing, and the tests it found asserting nothing

**Decision.** Every phase changes one thing at a time — remove a check, invert a
comparison, delete a call — and records how many tests die. **The kill count and
the named victims are predicted before the run**, and the prediction is written
down first.

**Alternative.** Coverage as the quality signal.

**Why not.** Coverage says a line ran. It cannot say an assertion would have
noticed if the line were wrong.

**What it actually found.** Three classes of defect that review did not:

1. **Two tests that were green and asserting nothing.** They expected a 401 for
   an expired token and were getting one — for "user not found" instead. Neither
   died when the expiry check was removed.
2. **Two concurrency tests that could not observe the condition they named.**
   The stub HTTP handler returned an already-completed task, so awaiting it
   never yielded and nothing could interleave with a refresh. Removing the very
   lock they existed to test killed neither.
3. **Two false causal claims**, each shared by a specification and a code
   comment: a route constraint credited with disambiguation it does not perform
   (routing already ranked the literal segment first), and an
   `IgnoreQueryFilters()` call believed to be required that was inert, because
   nothing filtered participates in that query. Both times the code was
   defensible and the *stated reason* was false.

**The rule that came out of it.** A test whose subject is timing, transport or
concurrency must contain the mechanism that forces the condition — because the
default behaviour of every convenient test double is to remove it. A completed
task removes interleaving. A negotiated transport removes the transport. Neither
is visible in the test body.

**Trade-off admitted.** It is slow and manual, there is no mutation framework in
the loop, and predicting first is tempting to skip. A prediction written after
seeing the number is worth nothing, and the discipline is the only thing
preventing it.

---

## 13. Two defective commits are not rewritten

**Decision.** Two commits in the pushed history are broken. Both stand.

| Commit | What is wrong |
|---|---|
| `ef46375` | A project entered the solution before the `.csproj` it references existed — **cannot restore** |
| `7cb6923` | Restores and compiles perfectly; an interface was registered with no implementation, so the container **cannot be built and the API will not start** |

**Alternative.** Rebase and force-push. Both would take minutes.

**Why not.** This repository *is* the submission and its history is offered as
evidence. Rewriting it to look tidier is editing the evidence, and a history
that has been groomed says less about how the work went than one that has not.

**What the second one taught, which is the reason it is in this document.** A
build-only check passes `7cb6923`. **Composition is not a compile-time
property**, so a rule phrased in terms of compilation cannot express the
requirement. The rule is now: every commit restores, builds, passes its tests
**and starts** — and it is enforced by `scripts/verify-history.sh` rather than
by remembering, because a remembered rule is exactly what failed. The script was
calibrated against both of these commits before its output was trusted.

The ordering mattered as much as the check. Verification originally ran *after*
the push, which found nothing twice by luck. It now runs before, where a bad
commit is one squash away from fixed.

**Trade-off admitted.** An evaluator who checks out either commit finds a broken
tree, and the explanation is in a file rather than in the history. Verifying
every commit also costs minutes per push and starts a database container each
time.

---

## 14. A walking skeleton before the real client

**Decision.** Before building the desktop clients properly, a deliberately
minimal one: sign in, show a queue, nothing else. Thrown away and rewritten
later.

**Alternative.** Go straight to the real client.

**Why.** It found a bug that unit tests structurally could not. **320 tests were
green while the queue listed every patient twice.** The shell reacted to sign-in
with an `async` lambda on an event — fire and forget, nothing awaiting it — so
two refreshes raced, each clearing the rows and then adding its own.
`AllowConcurrentExecutions = false` had not helped, because it guards the
*command* and the shell was calling the *method*.

Every view model was individually correct. Unit tests verify components; they
cannot see composition.

**Trade-off admitted.** The skeleton is code the real client largely replaced.
That is the price, and it came to one bug's worth — which was cheap, but it is
not a guarantee the next one would be.

**A second instance, later and worse.** SignalR delivers on a thread-pool
thread; the view models mutate collections the windows bind to; and nothing
marshalled to the UI thread. Three phases of tests missed it because the unit
tests raise events on the test's own thread and the end-to-end drive was a
console program with no UI thread to be on the wrong side of. It was found by
asking the question explicitly rather than by any test, measured (a handler on
thread 11, a connection created on thread 4), and fixed with a dispatcher
abstraction the shells implement in one line.

---

## 15. Structural enforcement over remembered rules

**Not a single decision so much as the through-line**, and the honest place to
end.

| Rule | How it is enforced |
|---|---|
| `Domain` reads no clock and no random source | A banned-API analyzer fails the build |
| An assistant never receives a diagnosis | The type has no such member |
| An assistant application cannot request one | The interface declares no such method |
| Auditing cannot be forgotten | It is an interceptor, not a call |
| Every endpoint requires authentication | A fallback policy; opening one is deliberate |
| A doctor may only touch their own queue | Checked in the application service, where a new controller cannot skip it |
| Every commit builds, tests and starts | A script, run before every push |
| XAML deprecations fail the build | 22 diagnostic codes promoted in `.editorconfig` |

**The trade-off, stated once for all of them.** Structural enforcement is more
work up front and less flexible afterwards. It also produces some things that
look like over-engineering in isolation — two nearly identical DTOs, an
interface with one implementation, a script that re-runs the whole suite per
commit. Each is answering "what happens when somebody forgets", and the answer
in this system is meant to be "they cannot".

Where that was impossible — the audit redaction, which must show one role what
it hides from another — it is said out loud rather than papered over. That is
entry 5, and it is the exception that defines the rule.
