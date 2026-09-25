#!/bin/sh
# Mirror azure main to the public GitHub repository as a rewritten copy.
#
# GitHub stopped at BASE (2026-09-01). Every commit after BASE is rewritten
# in a throwaway bare clone:
#   - the two ITU source folders below are removed from every commit: full
#     Recommendation texts and working documents are not published from
#     this repository (same rule as docs/Maral and docs/RecS.1503-4);
#   - author and committer email become the GitHub noreply address.
# Messages, names, dates and every other file are kept, so the rewrite is
# deterministic: rerun after new commits, it reproduces the hashes already
# on GitHub and the push is a fast-forward. Azure and the working tree are
# not touched.
#
# Usage, from anywhere inside the repository:
#   sh tests/github-mirror.sh         rewrite and verify only
#   sh tests/github-mirror.sh push    rewrite, verify, push to GitHub main
set -eu

BASE=2f5b2ca3d8a3392a47dcb15f21b939f750aa1d27
# The user name in the URL makes the credential manager pick the
# TimKadyrov account when the box also holds another GitHub account.
URL=https://TimKadyrov@github.com/TimKadyrov/radians.beamlab.git
EMAIL=81794596+TimKadyrov@users.noreply.github.com
D1='docs/REC-S.1325 rev'
D2='docs/REC-S.1328 Databank'
export EMAIL D1 D2

REPO=$(git rev-parse --show-toplevel)
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
WORK="$TMP/mirror.git"
git clone -q --bare --no-tags --single-branch --branch main "$REPO" "$WORK"
cd "$WORK"
SRC=$(git rev-parse main)
git merge-base --is-ancestor "$BASE" "$SRC" || { echo "BASE is not an ancestor of main" >&2; exit 1; }

FILTER_BRANCH_SQUELCH_WARNING=1 git filter-branch -f \
    --env-filter 'GIT_AUTHOR_EMAIL=$EMAIL; GIT_COMMITTER_EMAIL=$EMAIL; export GIT_AUTHOR_EMAIL GIT_COMMITTER_EMAIL' \
    --index-filter 'git rm -r -q --cached --ignore-unmatch -- "$D1" "$D2"' \
    -- "$BASE..main" >/dev/null 2>&1
OUT=$(git rev-parse main)

fail() { echo "CHECK FAILED: $*" >&2; exit 1; }
# Same number of commits, on top of BASE, messages unchanged.
[ "$(git rev-list --count "$BASE..$SRC")" = "$(git rev-list --count "$BASE..$OUT")" ] || fail "commit count"
git merge-base --is-ancestor "$BASE" "$OUT" || fail "not on top of BASE"
[ "$(git log --format=%B "$BASE..$SRC")" = "$(git log --format=%B "$BASE..$OUT")" ] || fail "messages differ"
# The two folders are in no rewritten commit.
[ -z "$(git rev-list "$BASE..$OUT" -- "$D1" "$D2")" ] || fail "a rewritten commit still carries a dropped folder"
# The tip differs from azure main by exactly the dropped files.
[ "$(git diff --name-only "$SRC" "$OUT")" = "$(git ls-tree -r --name-only "$SRC" -- "$D1" "$D2")" ] || fail "tree differs beyond the dropped folders"
# One email only.
[ "$(git log --format='%ae%n%ce' "$BASE..$OUT" | sort -u)" = "$EMAIL" ] || fail "emails"

echo "azure main   $SRC"
echo "github copy  $OUT"
echo "commits      $(git rev-list --count "$BASE..$OUT") after BASE ${BASE%"${BASE#???????}"}"
echo "dropped      $(git ls-tree -r --name-only "$SRC" -- "$D1" "$D2" | wc -l | tr -d ' ') files in '$D1' and '$D2'"
echo "email        $EMAIL"

[ "${1:-}" = push ] || { echo "verified; not pushed (pass 'push' to push)"; exit 0; }
TIP=$(git ls-remote "$URL" refs/heads/main | cut -f1)
[ -n "$TIP" ] || fail "cannot read GitHub main"
git cat-file -e "$TIP^{commit}" 2>/dev/null && git merge-base --is-ancestor "$TIP" "$OUT" \
    || fail "GitHub main $TIP is not in the rewritten history: not a fast-forward, nothing pushed"
git push "$URL" "$OUT:refs/heads/main"
