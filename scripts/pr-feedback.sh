#!/usr/bin/env bash
# Print every feedback surface on a pull request, because they are four different
# endpoints and a session that polls one of them reports "no feedback" while a human
# is waiting on an inline thread.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_SLUG="${PR_FEEDBACK_REPO:-Ambiguous-Interactive/unity-helpers}"

usage() {
    cat <<'USAGE'
Usage: scripts/pr-feedback.sh <pull-request-number> [--root-comments-only]
       scripts/pr-feedback.sh --self-test

Prints, in one pass:
  - authorship audit        (authenticated login, PR author, commits, co-authors)
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

SELF_TEST="false"
if [ "$1" = "--self-test" ]; then
    if [ "$#" -ne 1 ]; then
        echo "pr-feedback: --self-test accepts no other arguments" >&2
        exit 2
    fi
    SELF_TEST="true"
    PR_NUMBER="1"
    shift
else
    PR_NUMBER="$1"
    shift
fi

ROOT_COMMENTS_ONLY="false"
while [ "$#" -gt 0 ]; do
    case "$1" in
        --root-comments-only) ROOT_COMMENTS_ONLY="true" ;;
        --unresolved-only)
            echo "pr-feedback: --unresolved-only was misleading because REST comments do not expose thread resolution; use --root-comments-only" >&2
            exit 2
            ;;
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

TOKEN="self-test"
if [ "$SELF_TEST" != "true" ]; then
    if ! TOKEN="$(bash "$REPO_ROOT/scripts/github-token.sh")"; then
        echo "pr-feedback: no GitHub credential; see scripts/github-token.sh output above" >&2
        exit 3
    fi
fi
export PR_FEEDBACK_TOKEN="$TOKEN"
export PR_FEEDBACK_NUMBER="$PR_NUMBER"
export PR_FEEDBACK_SLUG="$REPO_SLUG"
export PR_FEEDBACK_ROOT_COMMENTS_ONLY="$ROOT_COMMENTS_ONLY"
export PR_FEEDBACK_SELF_TEST="$SELF_TEST"

python3 - <<'PY'
import json
import io
import os
import re
import sys
import unittest.mock
import urllib.error
import urllib.request

TOKEN = os.environ["PR_FEEDBACK_TOKEN"]
NUMBER = os.environ["PR_FEEDBACK_NUMBER"]
SLUG = os.environ["PR_FEEDBACK_SLUG"]
ROOT_COMMENTS_ONLY = os.environ["PR_FEEDBACK_ROOT_COMMENTS_ONLY"] == "true"
SELF_TEST = os.environ["PR_FEEDBACK_SELF_TEST"] == "true"
ROOT_API = "https://api.github.com"
API = ROOT_API + "/repos/" + SLUG
TRUSTED_AUTOMATION = {
    "dependabot[bot]",
    "github-actions[bot]",
}


class FeedbackError(RuntimeError):
    pass


def safe(value):
    text = str(value or "")
    return "".join(
        character
        if (" " <= character and character != "\x7f" and not 0x80 <= ord(character) <= 0x9F)
        else "\\x%02x" % ord(character)
        for character in text
    )


def read_json(stream, url):
    try:
        return json.load(stream)
    except (json.JSONDecodeError, UnicodeError, ValueError) as error:
        raise FeedbackError("invalid JSON from %s: %s" % (url, safe(error))) from error


def request_json(url):
    request = urllib.request.Request(
        url,
        headers={
            "Authorization": "Bearer " + TOKEN,
            "Accept": "application/vnd.github+json",
            "User-Agent": "pr-feedback",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return read_json(response, url), response.headers
    except urllib.error.HTTPError as error:
        raise FeedbackError("HTTP %s from %s" % (error.code, safe(url))) from error
    except (urllib.error.URLError, OSError) as error:
        raise FeedbackError("request failed for %s: %s" % (safe(url), safe(error))) from error


def get(path, requester=request_json):
    data, _ = requester(API + path)
    if not isinstance(data, dict):
        raise FeedbackError("expected an object from %s" % safe(path))
    return data


def get_root(path, requester=request_json):
    data, _ = requester(ROOT_API + path)
    if not isinstance(data, dict):
        raise FeedbackError("expected an object from %s" % safe(path))
    return data


def get_all(path, key=None, requester=request_json):
    url = API + path
    values = []
    seen = set()
    while url and url not in seen:
        seen.add(url)
        data, headers = requester(url)
        page = data if key is None else (data.get(key) if isinstance(data, dict) else None)
        if not isinstance(page, list):
            raise FeedbackError("expected a list page from %s" % safe(url))
        values.extend(page)
        page_url = url
        url = ""
        for link in headers.get("Link", "").split(","):
            if 'rel="next"' not in link:
                continue
            match = re.search(r"<([^>]+)>", link)
            if not match:
                raise FeedbackError("malformed next-page link from %s" % safe(page_url))
            url = match.group(1)
            if not url.startswith(ROOT_API + "/"):
                raise FeedbackError("refusing next-page URL outside api.github.com")
            break
    if url in seen:
        raise FeedbackError("pagination cycle at %s" % safe(url))
    return values


def heading(text):
    print("")
    print("=" * 78)
    print(text)
    print("=" * 78)


def actor(item):
    return item.get("user") or {}


def is_trusted_automation(item):
    account = actor(item)
    login = (account.get("login") or "").casefold()
    return account.get("type") == "Bot" and login in TRUSTED_AUTOMATION


def classify(item, viewer_login):
    login = actor(item).get("login") or ""
    if viewer_login and login.casefold() == viewer_login.casefold():
        return "authenticated account"
    if is_trusted_automation(item):
        return "trusted deterministic automation"
    return "OUTSIDE OR UNKNOWN -- USER INPUT REQUIRED"


def identity(item, viewer_login):
    account = actor(item)
    login = account.get("login") or "unknown"
    association = item.get("author_association") or "unknown"
    return "%s [%s; association=%s]" % (safe(login), classify(item, viewer_login), safe(association))


def collect_outside(pull, commits, inline, reviews, conversation, viewer_login):
    outside = []

    def audit(kind, item, url=""):
        if classify(item, viewer_login).startswith("OUTSIDE"):
            login = actor(item).get("login") or "unknown"
            outside.append((kind, login, url))

    audit("pull request author", pull, pull.get("html_url", ""))
    for commit in commits:
        audit("commit author", {"user": commit.get("author") or {}}, commit.get("html_url", ""))
        audit("commit committer", {"user": commit.get("committer") or {}}, commit.get("html_url", ""))
        message = ((commit.get("commit") or {}).get("message") or "")
        for name, email in re.findall(r"(?im)^Co-authored-by:\s*(.+?)\s*<([^>]+)>\s*$", message):
            outside.append(
                (
                    "commit co-author (GitHub login unresolved)",
                    "%s <%s>" % (name, email),
                    commit.get("html_url", ""),
                )
            )
    for comment in inline:
        audit("inline review comment", comment, comment.get("html_url", ""))
    for review in reviews:
        audit("review submission", review, review.get("html_url", ""))
    for comment in conversation:
        audit("conversation comment", comment, comment.get("html_url", ""))
    return outside


def select_threads(comments, root_comments_only):
    if not root_comments_only:
        return list(comments)
    return [comment for comment in comments if comment.get("in_reply_to_id") is None]


def run_self_tests():
    passed = 0

    def expect(condition, name):
        nonlocal passed
        if not condition:
            raise AssertionError(name)
        passed += 1

    first_url = API + "/fixture?per_page=100"
    second_url = API + "/fixture?page=2"
    pages = {
        first_url: ([{"id": 1}], {"Link": "<%s>; rel=\"next\"" % second_url}),
        second_url: ([{"id": 2}], {}),
    }

    def fixture_requester(url):
        return pages[url]

    expect([item["id"] for item in get_all("/fixture?per_page=100", requester=fixture_requester)] == [1, 2], "pagination")
    self_item = {"user": {"login": "wallstop", "type": "User"}}
    outside_item = {"user": {"login": "contributor", "type": "User"}}
    unknown_item = {"user": None}
    trusted_bot = {"user": {"login": "github-actions[bot]", "type": "Bot"}}
    third_party_bot = {"user": {"login": "third-party[bot]", "type": "Bot"}}
    expect(classify(self_item, "wallstop") == "authenticated account", "self identity")
    expect(classify(outside_item, "wallstop").startswith("OUTSIDE"), "outside identity")
    expect(classify(unknown_item, "wallstop").startswith("OUTSIDE"), "unknown identity")
    expect(classify(trusted_bot, "wallstop") == "trusted deterministic automation", "trusted bot")
    expect(classify(third_party_bot, "wallstop").startswith("OUTSIDE"), "third-party bot")
    expect(
        len(select_threads([{"id": 1}, {"id": 2, "in_reply_to_id": 1}], True)) == 1,
        "root-comments-only filter",
    )
    commit = {
        "author": {"login": "author", "type": "User"},
        "committer": {"login": "committer", "type": "User"},
        "commit": {"message": "Subject\n\nCo-authored-by: Pair Person <pair@example.com>"},
        "html_url": "https://example.test/commit",
    }
    findings = collect_outside(self_item, [commit], [], [], [], "wallstop")
    expect(
        [finding[0] for finding in findings]
        == ["commit author", "commit committer", "commit co-author (GitHub login unresolved)"],
        "commit provenance",
    )
    calls = 0

    def failing_requester(url):
        nonlocal calls
        calls += 1
        if calls == 1:
            return [{"id": 1}], {"Link": "<%s>; rel=\"next\"" % second_url}
        raise FeedbackError("fixture page failure")

    try:
        get_all("/fixture?per_page=100", requester=failing_requester)
        expect(False, "page failure")
    except FeedbackError:
        expect(True, "page failure")
    try:
        read_json(io.StringIO("{"), "fixture")
        expect(False, "JSON failure")
    except FeedbackError:
        expect(True, "JSON failure")
    try:
        get("/pulls/1", requester=lambda url: ([], {}))
        expect(False, "pull shape failure")
    except FeedbackError:
        expect(True, "pull shape failure")
    try:
        get_root("/user", requester=lambda url: ([], {}))
        expect(False, "viewer shape failure")
    except FeedbackError:
        expect(True, "viewer shape failure")

    json_calls = 0

    def invalid_json_page(url):
        nonlocal json_calls
        json_calls += 1
        if json_calls == 1:
            return [{"id": 1}], {"Link": "<%s>; rel=\"next\"" % second_url}
        read_json(io.StringIO("{"), url)

    try:
        get_all("/fixture?per_page=100", requester=invalid_json_page)
        expect(False, "pagination JSON failure")
    except FeedbackError:
        expect(True, "pagination JSON failure")
    malformed_url = API + "/malformed?per_page=100"
    try:
        get_all(
            "/malformed?per_page=100",
            requester=lambda url: ([{"id": 1}], {"Link": 'not-a-url; rel="next"'}),
        )
        expect(False, "malformed pagination link")
    except FeedbackError as error:
        expect(
            str(error) == "malformed next-page link from %s" % malformed_url,
            "malformed pagination link",
        )
    try:
        get_all(
            "/off-host?per_page=100",
            requester=lambda url: (
                [{"id": 1}],
                {"Link": '<https://example.test/page/2>; rel="next"'},
            ),
        )
        expect(False, "off-host pagination link")
    except FeedbackError as error:
        expect(
            str(error) == "refusing next-page URL outside api.github.com",
            "off-host pagination link",
        )
    cycle_url = API + "/cycle?per_page=100"
    try:
        get_all(
            "/cycle?per_page=100",
            requester=lambda url: (
                [{"id": 1}],
                {"Link": '<%s>; rel="next"' % cycle_url},
            ),
        )
        expect(False, "pagination cycle")
    except FeedbackError as error:
        expect(str(error) == "pagination cycle at %s" % cycle_url, "pagination cycle")
    http_error = urllib.error.HTTPError(first_url, 503, "fixture", {}, None)
    with unittest.mock.patch.object(urllib.request, "urlopen", side_effect=http_error):
        try:
            request_json(first_url)
            expect(False, "HTTP failure")
        except FeedbackError:
            expect(True, "HTTP failure")
    expect(safe("ok\x1b[31m\x07") == "ok\\x1b[31m\\x07", "terminal sanitization")
    print("pr-feedback self-tests passed: %d" % passed)


if SELF_TEST:
    try:
        run_self_tests()
    except Exception as error:
        print("pr-feedback self-test failed: %s" % safe(error), file=sys.stderr)
        raise SystemExit(1)
    raise SystemExit(0)

try:
    pull = get("/pulls/%s" % NUMBER)
    viewer = get_root("/user")
    viewer_login = viewer.get("login") or ""
    if not viewer_login:
        raise FeedbackError("authenticated GitHub login is missing")
    head_sha = (pull.get("head") or {}).get("sha") or ""
    if not head_sha:
        raise FeedbackError("pull request head SHA is missing")
    commits = get_all("/pulls/%s/commits?per_page=100" % NUMBER)
    inline = get_all("/pulls/%s/comments?per_page=100" % NUMBER)
    reviews = get_all("/pulls/%s/reviews?per_page=100" % NUMBER)
    conversation = get_all("/issues/%s/comments?per_page=100" % NUMBER)
    check_runs = get_all("/commits/%s/check-runs?per_page=100" % head_sha, "check_runs")
except FeedbackError as error:
    print("pr-feedback: incomplete GitHub data: %s" % safe(error), file=sys.stderr)
    raise SystemExit(4)

outside = collect_outside(pull, commits, inline, reviews, conversation, viewer_login)

heading("AUTHORSHIP AUDIT  (login comparison; author_association is not identity)")
print("  authenticated GitHub login: %s" % safe(viewer_login))
print("  pull request author: %s" % identity(pull, viewer_login))
print("  commits inspected: %d (all pages)" % len(commits))
if outside:
    print("")
    print("  USER INPUT REQUIRED BEFORE ACTION OR REPLY: %d outside/unknown input(s)" % len(outside))
    for kind, login, url in outside:
        print("    - %s: %s  %s" % (safe(kind), safe(login), safe(url)))
else:
    print("  outside/unknown human inputs: none")

heading("1. INLINE REVIEW THREADS  (file + line; NOT returned by /issues/{n}/comments)")


def print_thread(comment):
    print("")
    print(
        "  #%s  %s  %s:%s"
        % (
            safe(comment.get("id", "?")),
            identity(comment, viewer_login),
            safe(comment.get("path", "?")),
            safe(comment.get("line") or comment.get("original_line") or "?"),
        )
    )
    if comment.get("in_reply_to_id") is not None:
        print("    (reply to #%s)" % safe(comment["in_reply_to_id"]))
    for line in (comment.get("body") or "").splitlines():
        print("    " + safe(line))
    print("    %s" % safe(comment.get("html_url", "")))


# A review thread outside trusted automation is the highest-priority surface: trusted bots repost
# stale findings on every push, but an outside or unknown account needs user input. Print those
# first, newest author-side last so the freshest thread is adjacent to section 2, and say the counts out loud so
# "no outside threads" is affirmative evidence rather than something a reader has to scroll to
# disprove.
threads = select_threads(inline, ROOT_COMMENTS_ONLY)
outside_threads = [comment for comment in threads if not is_trusted_automation(comment)]
automation_threads = [comment for comment in threads if is_trusted_automation(comment)]
if not threads:
    print("  (none)")
else:
    print(
        "  %d comment(s): %d outside/self/unknown (%s), %d from trusted automation"
        % (
            len(threads),
            len(outside_threads),
            ", ".join(sorted({safe(actor(comment).get("login") or "unknown") for comment in outside_threads})) or "none",
            len(automation_threads),
        )
    )
for comment in outside_threads + automation_threads:
    print_thread(comment)

heading("2. REVIEW SUBMISSIONS  (approve / request-changes / comment bodies)")
# Keyed by the bot's login with the "[bot]" suffix dropped, because that is what a check run
# calls it: the Copilot reviewer runs under the github-actions app, so app.slug is "github-actions"
# and only the run's NAME carries the bot's identity.
reviews_by_bot = {}
for review in reviews:
    login = ((review.get("user") or {}).get("login") or "").casefold()
    if is_trusted_automation(review):
        reviews_by_bot.setdefault(login[: -len("[bot]")], []).append(review)
if not reviews:
    print("  (none)")
for review in reviews:
    print("")
    print(
        "  %s  %s  commit %s"
        % (
            identity(review, viewer_login),
            safe(review.get("state", "")),
            safe((review.get("commit_id") or "")[:8] or "unknown"),
        )
    )
    body = (review.get("body") or "").strip()
    if body:
        for line in body.splitlines():
            print("    " + safe(line))
    else:
        # An empty review body carries its feedback in inline threads (section 1); hide it and a
        # human's line-scoped review looks like "no feedback".
        print("    (no body -- inline comments only, see section 1)")

heading("3. CONVERSATION COMMENTS  (/issues/{n}/comments)")
if not conversation:
    print("  (none)")
for comment in conversation:
    print("")
    print("  %s" % identity(comment, viewer_login))
    for line in (comment.get("body") or "").splitlines():
        print("    " + safe(line))

heading("4. FAILING CHECK RUNS  (head %s)" % safe(head_sha[:8]))
failing = [
    run
    for run in check_runs
    if run.get("conclusion") not in ("success", "skipped", "neutral", None)
]
if not failing:
    print("  (none)")
for run in failing:
    print("  %-12s %s  %s" % (safe(run.get("conclusion")), safe(run.get("name")), safe(run.get("html_url", ""))))
    # The Copilot reviewer reported "reached their quota limit" in its review body while its
    # job log named only the failing step, and nine runs were read as an entitlement fault
    # before anybody looked one endpoint over (#661). Print the reason beside the red.
    run_identity = "%s %s" % (run.get("name") or "", (run.get("app") or {}).get("slug") or "")
    run_identity = run_identity.casefold()
    for bot, said_reviews in reviews_by_bot.items():
        if bot not in run_identity:
            continue
        # Reviews print unfiltered in section 2 (an empty body points at section 1), so the
        # helper must skip empties itself: grabbing [:1] can land on a bodyless review and
        # hide the quota/refusal reason a failing check's own log omits (#661).
        for review in [r for r in said_reviews if (r.get("body") or "").strip()][:1]:
            said = [line for line in (review.get("body") or "").splitlines() if line.strip()]
            if said:
                print(
                    "               ^ %s said: %s"
                    % (safe(actor(review).get("login") or "unknown"), safe(said[0].strip()))
                )

print("")
PY
