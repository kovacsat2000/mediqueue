# Demo script

**Ten minutes of live demonstration inside a thirty-minute slot.** Ordered
beats, the exact action, and the sentence to say. Timings are cumulative and
generous; the whole thing runs in about eight, which leaves room for
interruptions — and interruptions are the good outcome.

> **Rule for the day:** if something does not respond within five seconds, say
> the fallback sentence and move on. Every beat below has one. Nothing here is
> worth debugging in front of an audience.

---

## Before they arrive — the setup checklist

Do this in order. It takes about three minutes and it is the difference between
a demo and an incident.

```bash
# 1. Database first, always. Nothing else starts without it.
docker compose up -d db

# 2. Fresh seed, so the queues are short and the story is legible.
#    Skip if you have already rehearsed on this data and it looks right.
docker compose down -v && docker compose up -d db

# 3. The API. Wait for "Now listening on: http://localhost:5123".
dotnet run --project src/MediQueue.Api

# 4. Prove the whole thing works before anybody is watching.
python3 scripts/smoke_test.py
```

**If the smoke test does not print "All checks passed", stop and fix it now.**
It exercises every beat below. A green smoke test means the demonstration is a
formality.

Then, and only then:

```bash
dotnet run --project src/MediQueue.Client.Assistant
dotnet run --project src/MediQueue.Client.Doctor
```

**Window layout.** Assistant on the left, doctor on the right, both visible at
once. Sign in **before** they arrive: `horvath.anna` on the left,
`kovacs.istvan` on the right, password `MediQueue123!` for both. A third
terminal with Scalar open at `http://localhost:5123/scalar/` on another desktop.

**Have ready but not showing:** `src/MediQueue.Contracts/Visits/VisitSummaryDto.cs`
open in the editor, and this file's §"the audit beat" JSON if the live version
misbehaves.

---

## The script

### 0 · 00:00 — What it is (30 seconds, no clicks)

> "A practice with several doctors. Assistants register patients and choose a
> specialty; **the server** picks which doctor. Doctors call patients in, record
> a diagnosis, release them. Everything is pushed to both screens live, every
> change is audited, and an assistant can see that a diagnosis was recorded but
> never what it says. That last part is the interesting one and I will come back
> to it."

Both windows already visible. Do not narrate the layout; they can see it.

---

### 1 · 00:30 — The push, which is the whole demo in one action (90 seconds)

**Do:** In the **assistant** window, register a patient into **Belgyógyászat**.

- Name `Tóth Erzsébet`, address `1052 Budapest, Váci utca 12.`, TAJ
  `123-456-788` — *(any well-formed TAJ not already seeded; if it is refused as
  a duplicate, change one digit)* — complaint `Fejfájás és szédülés`, specialty
  **Belgyógyászat**.
- Press **Register**.

**Say, while pointing at the right-hand window:**

> "I have not touched the doctor's window. It is a separate process, on a
> WebSocket, and the row arrived because the server told it to."

**Then:** register a second patient into the same specialty.

> "Note which doctor each one went to. The assistant never chose — the server
> did, by shortest queue. It is the only genuinely algorithmic rule in the
> assignment, and it is behind an interface so a different policy is a
> registration change rather than an edit."

**If the row does not appear:** press **Refresh** in the doctor window and say —

> "The push has not arrived; the Refresh button is the recovery path, and it is
> in the client precisely because a failed push must never fail a committed
> write. The patient is in the database either way."

Then carry on. Everything below still works.

---

### 2 · 02:00 — The clinical path (90 seconds)

**Do:** In the **doctor** window, select Tóth Erzsébet and press **Call in**.

**Say:**

> "Watch the left-hand window." *(the assistant's copy updates)* "Both screens
> are tracking the same fact from the same event."

**Do:** Type a diagnosis — `Migrén, feszültséges eredetű. Pihenés javasolt.` —
and press **Record diagnosis**. Then **Release**.

**Say:**

> "Four states, three legal transitions, and the state machine is a table rather
> than a pile of `if`s. The test asserts all sixteen ordered pairs — the three
> that are allowed and the thirteen that are not. A happy-path test would pass
> against a machine that allowed everything."

**If an action is refused:** read the message aloud from the screen.

> "That is the server's own sentence, not the client's. The client does not
> re-derive the state machine; it renders what the server said."

---

### 3 · 03:30 — The system refuses, and the patient is not lost (90 seconds)

**This beat is better than another success.**

**Do:** In the assistant window, register a patient into **Reumatológia**.

**Say, reading the refusal:**

> "Reumatológia has exactly one rheumatologist and she is deactivated — on
> purpose, in the seed, so this path is reachable at all. The message names the
> specialty rather than a GUID, because the person reading it is at a reception
> desk."

**Do:** Point at the **unrouted list** — she is in it.

> "And she is not lost. `Registered` is one of the four states the specification
> names, and it means *arrived, not yet routed*. An earlier version of this
> system could reach that state and had no screen that showed it, which is the
> same as not implementing it."

**Do:** Select her, choose **Belgyógyászat**, press **Route**.

> "Out of the unrouted list, into a doctor's queue, in one event. She is in
> exactly one list at every instant — that is the hardest thing about this
> screen and it is a remove-then-insert under a single lock."

**If routing fails:** say "the refusal is the interesting half; that already
worked" and move on.

---

### 4 · 05:00 — The diagnosis is unreachable, not filtered (60 seconds)

**Do:** Bring up `VisitSummaryDto.cs` in the editor. Scroll it once.

**Say:**

> "This is the type every assistant-facing endpoint returns, and it is the type
> of every push payload. **There is no diagnosis property.** Not nulled —
> absent. 'Remember to strip the diagnosis' is a rule somebody forgets in a new
> endpoint. A type that cannot carry the field cannot leak it."

**Do:** In Scalar, call `GET /api/visits/{id}` as the assistant and show the raw
JSON.

> "No `diagnosis` key at all. The integration test asserts on the raw document
> rather than a deserialised object — because deserialising into this type would
> have thrown the key away in silence and passed against a leaking server."

**Do (optional, if the client interface point lands):**

> "The same idea reaches the desktop client: the assistant application is handed
> an interface with no method that returns the detail type, so it cannot even
> express the request."

---

### 5 · 06:00 — The audit log, which is the strongest thing here (2 minutes)

**Do:** In Scalar, `GET /api/audit?patientId={the patient from beat 2}`, once
authorised as **`kovacs.istvan`**, then again as **`horvath.anna`**.

Show the two bodies side by side. Point at the same entry in both.

**Say:**

> "Same entry. Same id, same actor, same timestamp, same entity, same action,
> same field name. Read once with a doctor's token and once with an assistant's."

```
doctor:     "newValue": "Migrén, feszültséges eredetű. Pihenés javasolt.",  "redacted": false
assistant:  "newValue": "***",                                             "redacted": true
```

> "Everything about the event is visible to both: that a diagnosis was recorded,
> by whom, on which visit, at which instant. Only the value differs.
>
> This is where the assignment sets a trap for itself. It asks for a queryable
> audit log **and** forbids assistants from seeing diagnoses — and together
> those open a back door, because a naive audit log hands over at the back what
> the API withholds at the front. Field-level sensitivity is what closes it."

**Then the honest part, unprompted:**

> "And this is the **one** guarantee in the system enforced by a runtime branch
> rather than by a type — because the same field must be visible to one role and
> hidden from another, and no shape of type can express that. So it lives in
> exactly one method, it fails closed, and a mutant that reveals it to everyone
> kills four tests, two of which read the raw bytes on every page of a paged
> response."

**Two follow-ups if anyone pulls the thread:**

- `oldValue` is `***` for the assistant and `null` for the doctor. Deliberate:
  preserving the null would say *"this was the first diagnosis"* rather than
  *"the doctor revised their finding"*, which is clinical inference.
- The entries are written by an EF Core `SaveChanges` interceptor, not by the
  use cases — because a use case can forget and this one must not.

**If the audit call fails live:** paste the two bodies from your notes and say
"this is the captured output; the smoke test asserts it on every run."

---

### 6 · 08:00 — Close (30 seconds)

> "One thing I would point at if you read nothing else: the pattern is
> structural enforcement over remembered rules. The domain reads no clock and an
> analyzer fails the build if it tries. Auditing is an interceptor because a
> call site can be forgotten. The assistant's type has no diagnosis member. And
> where that was impossible — the audit redaction — the README says so out loud
> rather than papering over it.
>
> Two commits in the history do not build. They are named in the README with why
> they were not rewritten, and a script now checks every commit restores,
> builds, passes **and starts** — because one of those two compiles perfectly
> and cannot start, and composition is not a compile-time property."

---

## Held in reserve — only if asked, or if there is time

| Beat | The action | The one sentence |
|---|---|---|
| **TAJ checksum** | Flip `Validation:TajChecksumEnabled` on, restart, re-register | "Every seeded TAJ is checksum-valid, so nothing breaks — but the assignment's own example `123-123-123` now fails. The rule is built and tested; whether to enforce it is the customer's call, not mine." |
| **Returning patient** | Register the same TAJ twice | "One `Patient`, two `Visit`s. 'Done' is not a permanent property of a person — that is why the aggregates are split against the specification's literal wording." |
| **Optimistic concurrency** | Two doctor windows, call the same patient in twice | "PostgreSQL's `xmin` as the concurrency token. The second write loses deterministically and gets a 409, at no storage cost and with no database detail in the domain." |
| **The banned-API analyzer** | Add `DateTimeOffset.UtcNow` to a domain file, build | "The build fails. The domain reads no ambient state and that is proven rather than asserted." |
| **Invalid transition body** | Release an already-released visit | "409 carrying `currentStatus`, `attemptedStatus` and `allowedTransitions` as extension members — so a client renders 'already released' without parsing prose." |
| **Mutation testing** | `docs/decisions.md` §12 on screen | "It found two tests that were green and asserting nothing, and twice disproved a causal claim a specification and a code comment agreed on." |
| **The history** | `git log --oneline`, scrolled once | "The repository is the submission. The history was written to be read." |

---

## If everything falls over

The order to recover in, and what to say while doing it:

1. **The push is dead but the API is alive.** Use Refresh in both windows and
   carry on. Say the line from beat 1. Nothing after beat 1 depends on push.
2. **A client will not start.** Do the whole demonstration in Scalar. Every beat
   above except 1 and 2 is an API call, and beats 4 and 5 are *better* in
   Scalar because the raw JSON is the point.
3. **The API will not start.** `docker compose down -v && docker compose up -d
   db`, then restart it — the usual cause is a database that was not up when the
   API tried to migrate. While it comes up, talk through `docs/decisions.md`;
   the reasoning was written before the code and stands on its own.
4. **Nothing works at all.** Run `python3 scripts/smoke_test.py` and let the
   output tell you and the room which step failed. It names it. Then talk
   architecture from the README's diagram until it is fixed or the slot ends.

**Say the failure out loud when it happens.** A demonstration that recovers
visibly is better than one that never wobbles, and this whole project has been
argued on being honest about what is broken.
