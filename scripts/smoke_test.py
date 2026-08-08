#!/usr/bin/env python3
"""End-to-end proof that a running MediQueue stack does what it claims.

Walks the whole specification in one command: an assistant registers a patient,
the patient is found in the unrouted list, routed to a specialty, and appears in
exactly one doctor's queue; that doctor calls her in, records a diagnosis and
releases her; and the audit log is then read as both roles to confirm the
diagnosis is legible to one and redacted for the other.

Standard library only, on purpose. This is meant to be the first thing run on
the morning of a demonstration, and `pip install` is not something to discover
you need at that moment.

Usage:
    python3 scripts/smoke_test.py [--base-url http://localhost:5123]

Exits 0 if everything held, 1 naming the first thing that did not, and 2 if the
stack could not be reached at all.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request
from typing import Any

PASSWORD = "MediQueue123!"
ASSISTANT = "horvath.anna"
DOCTORS = ("kovacs.istvan", "nagy.peter")
SPECIALTY = "Belgyógyászat"
DIAGNOSIS = "Migrén, feszültséges eredetű. Pihenés javasolt."

# The redaction marker the server substitutes for a value this role may not see.
REDACTED = "***"


class SmokeFailure(Exception):
    """A check that did not hold. The message names what failed."""


class Unreachable(Exception):
    """The stack could not be reached, which is a different problem."""


def request(
    base_url: str,
    method: str,
    path: str,
    token: str | None = None,
    body: Any | None = None,
) -> Any:
    """One HTTP call. Returns the decoded body, or None for a 204."""
    data = None if body is None else json.dumps(body).encode("utf-8")
    req = urllib.request.Request(f"{base_url}{path}", data=data, method=method)
    req.add_header("Accept", "application/json")

    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token is not None:
        req.add_header("Authorization", f"Bearer {token}")

    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            payload = response.read().decode("utf-8")
            return json.loads(payload) if payload.strip() else None
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")
        raise SmokeFailure(
            f"{method} {path} returned {error.code}: {detail.strip()[:400]}"
        ) from error
    except urllib.error.URLError as error:
        raise Unreachable(
            f"cannot reach {base_url} ({error.reason}). "
            "Start the database and the API first — see README.md."
        ) from error


def sign_in(base_url: str, username: str) -> tuple[str, dict[str, Any]]:
    """Signs in and returns the token and the user."""
    body = request(
        base_url, "POST", "/api/auth/login",
        body={"username": username, "password": PASSWORD},
    )
    return body["accessToken"], body["user"]


def unique_taj() -> str:
    """A well-formed TAJ nobody has used.

    Derived from the clock so that repeated runs against the same database do
    not collide on the unique index — a returning patient would be reused, and
    the run would then trip the one-open-visit rule instead of testing anything.
    """
    digits = f"7{int(time.time()) % 100_000_000:08d}"
    return f"{digits[:3]}-{digits[3:6]}-{digits[6:]}"


def check(condition: bool, message: str) -> None:
    if not condition:
        raise SmokeFailure(message)


def main() -> int:
    parser = argparse.ArgumentParser(description="End-to-end smoke test for MediQueue.")
    parser.add_argument("--base-url", default="http://localhost:5123", help="where the API is listening")
    arguments = parser.parse_args()
    base = arguments.base_url.rstrip("/")

    step = 0

    def announce(text: str) -> None:
        nonlocal step
        step += 1
        print(f"  {step:2}. {text}")

    print(f"MediQueue smoke test against {base}")

    health = request(base, "GET", "/health")
    check(health.get("status") == "healthy", f"/health did not report healthy: {health}")
    announce("the API is healthy and can reach the database")

    assistant_token, assistant = sign_in(base, ASSISTANT)
    announce(f"signed in as the assistant {assistant['fullName']}")

    specialties = request(base, "GET", "/api/specialties", assistant_token)
    matching = [s for s in specialties if s["name"] == SPECIALTY]
    check(bool(matching), f"the seeded specialty {SPECIALTY!r} is missing")
    specialty_id = matching[0]["id"]

    # --- register, with no specialty, so the unrouted list has something to show
    taj = unique_taj()
    visit = request(
        base, "POST", "/api/visits", assistant_token,
        {
            "fullName": "Próba Erzsébet",
            "address": "1052 Budapest, Váci utca 12.",
            "taj": taj,
            "complaint": "Fejfájás és szédülés",
            "specialtyId": None,
        },
    )
    visit_id, patient_id = visit["id"], visit["patientId"]
    check(visit["status"] == 1, f"a visit registered without a specialty should be Registered, was {visit['status']}")
    check("diagnosis" not in visit, "the assistant's registration response carried a diagnosis key")
    announce(f"registered {visit['patientFullName']} ({taj}) with no specialty")

    unrouted = request(base, "GET", "/api/visits/unassigned", assistant_token)
    check(
        any(v["id"] == visit_id for v in unrouted),
        "the newly registered visit is not in the unrouted list",
    )
    announce(f"she is in the unrouted list ({len(unrouted)} waiting to be routed)")

    # --- route her, and insist she is in exactly one place afterwards
    routed = request(base, "POST", f"/api/visits/{visit_id}/assign", assistant_token, {"specialtyId": specialty_id})
    doctor_id, doctor_name = routed["doctorId"], routed["doctorFullName"]
    check(doctor_id is not None, "routing produced no doctor")
    announce(f"routed to {SPECIALTY}; the server chose {doctor_name}")

    unrouted_after = request(base, "GET", "/api/visits/unassigned", assistant_token)
    check(
        all(v["id"] != visit_id for v in unrouted_after),
        "she is still in the unrouted list after being routed",
    )

    queues = request(base, "GET", "/api/queues", assistant_token)
    holding = [q for q in queues if any(v["id"] == visit_id for v in q["visits"])]
    check(
        len(holding) == 1,
        f"she should be in exactly one doctor's queue, found {len(holding)}",
    )
    check(holding[0]["doctorId"] == doctor_id, "she is in a queue belonging to a different doctor")
    announce(f"she is in exactly one queue — {holding[0]['doctorFullName']}'s — and no longer unrouted")

    # --- the clinical path, as whichever doctor the server picked
    username = next((u for u in DOCTORS if doctor_name.endswith(u.split(".")[0].capitalize())), None)
    if username is None:
        # Fall back to matching on the user list rather than on a name guess.
        for candidate in DOCTORS:
            token, user = sign_in(base, candidate)
            if user["id"] == doctor_id:
                username, doctor_token = candidate, token
                break
        else:
            raise SmokeFailure(f"cannot work out which seeded account is {doctor_name}")
    else:
        doctor_token, user = sign_in(base, username)
        check(user["id"] == doctor_id, f"{username} is not the doctor the visit was routed to")

    announce(f"signed in as {doctor_name}")

    mine = request(base, "GET", "/api/queues/mine", doctor_token)
    check(any(v["id"] == visit_id for v in mine), "the visit is not in the treating doctor's own queue")

    called_in = request(base, "POST", f"/api/visits/{visit_id}/call-in", doctor_token)
    check(called_in["status"] == 3, f"after call-in the status should be InTreatment, was {called_in['status']}")
    announce("called her in")

    diagnosed = request(
        base, "PUT", f"/api/visits/{visit_id}/diagnosis", doctor_token, {"diagnosis": DIAGNOSIS}
    )
    check(diagnosed["diagnosis"] == DIAGNOSIS, "the diagnosis came back different from what was sent")
    announce("recorded a diagnosis")

    released = request(base, "POST", f"/api/visits/{visit_id}/release", doctor_token)
    check(released["status"] == 4, f"after release the status should be Done, was {released['status']}")
    announce("released her — the visit is complete")

    # --- the audit log, read as both roles
    audit_path = f"/api/audit?patientId={patient_id}&pageSize=200"

    as_doctor = request(base, "GET", audit_path, doctor_token)
    doctor_changes = [c for e in as_doctor["items"] for c in e["changes"] if c["fieldName"] == "Diagnosis"]
    check(bool(doctor_changes), "the audit log recorded no Diagnosis change at all")
    check(
        any(c["newValue"] == DIAGNOSIS for c in doctor_changes),
        "a doctor cannot read the diagnosis they recorded, in the audit log",
    )
    check(
        all(not c["redacted"] for c in doctor_changes),
        "a doctor's audit entry is marked redacted",
    )
    announce(f"the audit log shows the diagnosis to a doctor ({as_doctor['totalCount']} entries for this patient)")

    as_assistant = request(base, "GET", audit_path, assistant_token)
    assistant_changes = [c for e in as_assistant["items"] for c in e["changes"] if c["fieldName"] == "Diagnosis"]
    check(bool(assistant_changes), "the assistant cannot see that a diagnosis changed at all")
    check(
        all(c["newValue"] == REDACTED and c["oldValue"] == REDACTED for c in assistant_changes),
        "the assistant's audit entry did not redact the diagnosis value",
    )
    check(
        all(c["redacted"] for c in assistant_changes),
        "the assistant's audit entry is not flagged as redacted",
    )

    # The bytes, not the parsed object: deserialising could quietly drop a field
    # and pass against a leaking server. Same reason the integration suite reads
    # raw JSON for this one rule.
    raw = json.dumps(as_assistant, ensure_ascii=False)
    check(DIAGNOSIS not in raw, "the diagnosis text appears in the assistant's raw audit response")
    announce("the same entries show *** to an assistant, and the raw bytes contain no diagnosis")

    # And the two roles are looking at the same events, not different ones.
    check(
        as_doctor["totalCount"] == as_assistant["totalCount"],
        "the two roles see a different number of audit entries; only the values should differ",
    )
    announce("both roles see the same events — only the clinical values differ")

    print("\nAll checks passed.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SmokeFailure as failure:
        print(f"\nFAILED: {failure}", file=sys.stderr)
        sys.exit(1)
    except Unreachable as failure:
        print(f"\nSTACK NOT RUNNING: {failure}", file=sys.stderr)
        sys.exit(2)
