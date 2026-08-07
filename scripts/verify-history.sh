#!/usr/bin/env bash
#
# Verifies that every commit in a range builds, passes its tests, AND starts.
#
# Why this exists (D-63). Two commits in this repository's pushed history are
# defective, and they fail differently:
#
#   ef46375  cannot restore — a project was added to the solution before the
#            .csproj it references existed
#   7cb6923  restores and compiles perfectly, and the API cannot start — an
#            interface was registered with no implementation, which
#            ValidateOnBuild turns into a startup failure
#
# A build-only check catches the first and not the second, because composition
# is not a compile-time property. What catches both is running the full test
# suite: the integration suite boots the real host through WebApplicationFactory,
# so a container that cannot be built is a test failure rather than a surprise
# in production.
#
# Both defects were pushed before anyone looked. Verification therefore belongs
# BEFORE the push, where the fix is one squash away — that ordering is the part
# that actually failed, twice.
#
# Usage:
#   scripts/verify-history.sh                 # everything on HEAD not yet pushed
#   scripts/verify-history.sh origin/main..HEAD
#   scripts/verify-history.sh <commit>        # that one commit
#
# Requires Docker: the integration suite starts a PostgreSQL container per run.

set -uo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# `pwd -P` resolves the path physically. On macOS $TMPDIR is /var/folders/…,
# which is a symlink to /private/var/folders/…, and MSBuild follows project
# references through both — so the same .csproj is restored under two names and
# NuGet fails with "the file … already exists". Found by running this script
# against a commit that restores perfectly in a normal checkout.
readonly WORKTREE="$(cd "$(mktemp -d "${TMPDIR:-/tmp}/mediqueue-verify-XXXXXX")" && pwd -P)"

# A detached worktree rather than checking out in place: the working tree keeps
# whatever the author was doing, so an interrupted run cannot lose uncommitted
# work. Removed on every exit path, including failure and Ctrl-C.
cleanup() {
    git -C "$REPO_ROOT" worktree remove --force "$WORKTREE" >/dev/null 2>&1
    rm -rf "$WORKTREE" >/dev/null 2>&1
}
trap cleanup EXIT INT TERM

RANGE="${1:-}"

if [[ -z "$RANGE" ]]; then
    if git -C "$REPO_ROOT" rev-parse --verify --quiet '@{upstream}' >/dev/null; then
        RANGE='@{upstream}..HEAD'
    else
        echo "No upstream is configured for this branch; pass a range explicitly." >&2
        exit 2
    fi
fi

# A single commit is a valid argument, and is what you want when re-checking one
# failure without re-running the whole range.
if git -C "$REPO_ROOT" rev-parse --verify --quiet "${RANGE}^{commit}" >/dev/null 2>&1; then
    COMMITS="$(git -C "$REPO_ROOT" rev-parse "$RANGE")"
else
    COMMITS="$(git -C "$REPO_ROOT" rev-list --reverse "$RANGE")"
fi

if [[ -z "$COMMITS" ]]; then
    echo "Nothing to verify in '$RANGE'."
    exit 0
fi

TOTAL="$(echo "$COMMITS" | wc -l | tr -d ' ')"
echo "Verifying $TOTAL commit(s) in '$RANGE'."
echo "Each must restore, build, and pass the full suite — the integration tests boot the host."
echo

git -C "$REPO_ROOT" worktree add --detach "$WORKTREE" HEAD >/dev/null 2>&1 || {
    echo "Could not create a worktree at $WORKTREE" >&2
    exit 2
}

FAILURES=0
INDEX=0

for COMMIT in $COMMITS; do
    INDEX=$((INDEX + 1))
    SUBJECT="$(git -C "$REPO_ROOT" log -1 --format='%s' "$COMMIT")"
    SHORT="$(git -C "$REPO_ROOT" rev-parse --short "$COMMIT")"

    printf '[%d/%d] %s  %s\n' "$INDEX" "$TOTAL" "$SHORT" "$SUBJECT"

    git -C "$WORKTREE" checkout --detach --force "$COMMIT" >/dev/null 2>&1
    git -C "$WORKTREE" clean -xdfq >/dev/null 2>&1

    LOG="$WORKTREE/../verify-$SHORT.log"
    STAGE=""

    if ! dotnet restore "$WORKTREE" >"$LOG" 2>&1; then
        STAGE="restore"
    elif ! dotnet build "$WORKTREE" --no-restore >>"$LOG" 2>&1; then
        STAGE="build"
    elif ! dotnet test "$WORKTREE" --no-build >>"$LOG" 2>&1; then
        # The stage that catches a commit which compiles and cannot start.
        STAGE="test"
    fi

    if [[ -n "$STAGE" ]]; then
        FAILURES=$((FAILURES + 1))
        printf '        FAILED at %s\n' "$STAGE"

        # Enough of the reason to act on, without pasting a build log into a
        # terminal. The full log path follows.
        grep -oE '(error [A-Z]+[0-9]+: .*|Unable to resolve service for type .*|Failed! *- *Failed: *[0-9]+[^,]*)' "$LOG" \
            | sort -u | head -3 | sed 's/^/          /'
        printf '          full log: %s\n' "$LOG"
    else
        printf '        ok\n'
    fi
done

echo
if [[ "$FAILURES" -gt 0 ]]; then
    echo "$FAILURES of $TOTAL commit(s) failed. Do not push."
    exit 1
fi

echo "All $TOTAL commit(s) build, test and start."
