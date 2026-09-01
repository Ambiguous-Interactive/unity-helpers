#!/usr/bin/env bash
# Print every feedback surface on a pull request, because they are four different
# endpoints and a session that polls one of them reports "no feedback" while a human
# is waiting on an inline thread.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_SLUG="${PR_FEEDBACK_REPO:-Ambiguous-Interactive/unity-helpers}"

usage() {
    cat <<'USAGE'
Usage: scripts/pr-feedback.sh <pull-request-number> [--unresolved-only]

Prints, in one pass:
  1. inline review threads  (GET /pulls/{n}/comments)   <- the one a PR-comment poll misses
  2. review submissions     (GET /pulls/{n}/reviews)
  3. conversation comments  (GET /issues/{n}/comments)
  4. failing check runs     (GET /commits/{sha}/check-runs), each with the review body its
                            own bot posted, when there is one -- a bot that refuses in a review
                            rather than in its job log is invisible to a check-run poll (#661)

Reply to an inline thread with the NUMERIC comment id this prints (the number in the
#discussion_r... anchor), never a PRRT_ GraphQL node id:

  POST /repos/<owner>/<repo>/pulls/<n>/comments/<comment-id>/replies

Environment:
  PR_FEEDBACK_REPO   owner/repo to query (default: Ambiguous-Interactive/unity-helpers)
USAGE
}

if [ "$#" -lt 1 ] || [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
    usage
    exit 0
fi

PR_NUMBER="$1"
shift
UNRESOLVED_ONLY="false"
while [ "$#" -gt 0 ]; do
    case "$1" in
        --unresolved-only) UNRESOLVED_ONLY="true" ;;
        *)
            echo "pr-feedback: unknown argument '$1'" >&2
            usage >&2
            exit 2
            ;;
    esac
    shift
done

case "$PR_NUMBER" in
    '' | *[!0-9]*)
        echo "pr-feedback: pull request number must be numeric, got '$PR_NUMBER'" >&2
        exit 2
        ;;
esac

if ! TOKEN="$(bash "$REPO_ROOT/scripts/github-token.sh")"; then
    echo "pr-feedback: no GitHub credential; see scripts/github-token.sh output above" >&2
    exit 3
fi
export PR_FEEDBACK_TOKEN="$TOKEN"
export PR_FEEDBACK_NUMBER="$PR_NUMBER"
export PR_FEEDBACK_SLUG="$REPO_SLUG"
export PR_FEEDBACK_UNRESOLVED_ONLY="$UNRESOLVED_ONLY"

python3 - <<'PY'
import json
import os
import urllib.error
import urllib.request

TOKEN = os.environ["PR_FEEDBACK_TOKEN"]
NUMBER = os.environ["PR_FEEDBACK_NUMBER"]
SLUG = os.environ["PR_FEEDBACK_SLUG"]
UNRESOLVED_ONLY = os.environ["PR_FEEDBACK_UNRESOLVED_ONLY"] == "true"
API = "https://api.github.com/repos/" + SLUG


def get(path):
    request = urllib.request.Request(
        API + path,
        headers={
            "Authorization": "Bearer " + TOKEN,
            "Accept": "application/vnd.github+json",
            "User-Agent": "pr-feedback",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        print("  ! HTTP %s on %s" % (error.code, path))
        return []


def heading(text):
    print("")
    print("=" * 78)
    print(text)
    print("=" * 78)


pull = get("/pulls/%s" % NUMBER)
head_sha = pull.get("head", {}).get("sha", "") if isinstance(pull, dict) else ""

heading("1. INLINE REVIEW THREADS  (file + line; NOT returned by /issues/{n}/comments)")
inline = get("/pulls/%s/comments?per_page=100" % NUMBER)
if not inline:
    print("  (none)")
for comment in inline:
    if UNRESOLVED_ONLY and comment.get("in_reply_to_id") is not None:
        continue
    print("")
    print(
        "  #%s  %s  %s:%s"
        % (
            comment["id"],
            comment["user"]["login"],
            comment.get("path", "?"),
            comment.get("line") or comment.get("original_line") or "?",
        )
    )
    if comment.get("in_reply_to_id") is not None:
        print("    (reply to #%s)" % comment["in_reply_to_id"])
    for line in (comment.get("body") or "").splitlines():
        print("    " + line)
    print("    %s" % comment.get("html_url", ""))

heading("2. REVIEW SUBMISSIONS  (approve / request-changes / comment bodies)")
reviews = [r for r in get("/pulls/%s/reviews?per_page=100" % NUMBER) if (r.get("body") or "").strip()]
# Keyed by the bot's login with the "[bot]" suffix dropped, because that is what a check run
# calls it: the Copilot reviewer runs under the github-actions app, so app.slug is "github-actions"
# and only the run's NAME carries the bot's identity.
reviews_by_bot = {}
for review in reviews:
    login = ((review.get("user") or {}).get("login") or "").lower()
    if login.endswith("[bot]"):
        reviews_by_bot.setdefault(login[: -len("[bot]")], []).append(review)
if not reviews:
    print("  (none with a body)")
for review in reviews:
    print("")
    print("  %s  %s" % (review["user"]["login"], review.get("state", "")))
    for line in (review.get("body") or "").splitlines():
        print("    " + line)

heading("3. CONVERSATION COMMENTS  (/issues/{n}/comments)")
conversation = get("/issues/%s/comments?per_page=100" % NUMBER)
if not conversation:
    print("  (none)")
for comment in conversation:
    print("")
    print("  %s" % comment["user"]["login"])
    for line in (comment.get("body") or "").splitlines():
        print("    " + line)

heading("4. FAILING CHECK RUNS  (head %s)" % (head_sha[:8] or "unknown"))
if head_sha:
    runs = get("/commits/%s/check-runs?per_page=100" % head_sha)
    failing = [
        run
        for run in (runs.get("check_runs", []) if isinstance(runs, dict) else [])
        if run.get("conclusion") not in ("success", "skipped", "neutral", None)
    ]
    if not failing:
        print("  (none)")
    for run in failing:
        print("  %-12s %s  %s" % (run.get("conclusion"), run.get("name"), run.get("html_url", "")))
        # The Copilot reviewer reported "reached their quota limit" in its review body while its
        # job log named only the failing step, and nine runs were read as an entitlement fault
        # before anybody looked one endpoint over (#661). Print the reason beside the red.
        identity = "%s %s" % (run.get("name") or "", (run.get("app") or {}).get("slug") or "")
        identity = identity.lower()
        for bot, said_reviews in reviews_by_bot.items():
            if bot not in identity:
                continue
            for review in said_reviews[:1]:
                said = [line for line in (review.get("body") or "").splitlines() if line.strip()]
                if said:
                    print(
                        "               ^ %s said: %s"
                        % (review["user"]["login"], said[0].strip())
                    )
else:
    print("  (could not resolve head sha)")

print("")
PY
