"""Print all four pull-request feedback surfaces with authorship checks."""

from __future__ import annotations

import io
import json
import os
import re
import shutil
import subprocess
import sys
import unittest.mock
import urllib.error
import urllib.request
from collections.abc import Callable, Mapping
from email.message import Message
from pathlib import Path
from typing import IO, TYPE_CHECKING, TextIO, cast

if TYPE_CHECKING:
    from http.client import HTTPMessage

token = ""
number = "1"
SLUG = os.environ.get("PR_FEEDBACK_REPO", "Ambiguous-Interactive/unity-helpers")
root_comments_only = False
ROOT_API = "https://api.github.com"
API = ROOT_API + "/repos/" + SLUG
TRUSTED_AUTHORS = {
    "wallstop",
}
TRUSTED_AUTOMATION = {
    "coderabbitai[bot]",
    "cursor[bot]",
    "copilot-pull-request-reviewer[bot]",
    "copilot-swe-agent[bot]",
    "dependabot[bot]",
    "github-actions[bot]",
}


USAGE = """Usage: scripts/pr-feedback.sh <pull-request-number> [--root-comments-only]
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
  PR_FEEDBACK_REPO   owner/repo to query (default: Ambiguous-Interactive/unity-helpers)"""


Record = Mapping[str, object]
Requester = Callable[[str], tuple[object, Mapping[str, str]]]


class FeedbackError(RuntimeError):
    pass


def safe(value: object) -> str:
    text = str(value or "")
    return "".join(
        character
        if (character >= " " and character != "\x7f" and not 0x80 <= ord(character) <= 0x9F)
        else f"\\x{ord(character):02x}"
        for character in text
    )


def read_json(stream: TextIO, url: str) -> object:
    try:
        return cast("object", json.load(stream))
    except (json.JSONDecodeError, UnicodeError, ValueError) as error:
        raise FeedbackError(f"invalid JSON from {url}: {safe(error)}") from error


class RejectRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(
        self,
        req: urllib.request.Request,
        fp: IO[bytes],
        code: int,
        msg: str,
        headers: HTTPMessage,
        newurl: str,
    ) -> None:
        del msg, headers, newurl
        fp.close()
        raise FeedbackError(f"refusing HTTP {code} redirect from {safe(req.full_url)}")


OPENER = urllib.request.build_opener(RejectRedirects())


def request_json(url: str) -> tuple[object, Mapping[str, str]]:
    if not url.startswith(ROOT_API + "/"):
        raise FeedbackError("refusing request URL outside api.github.com")
    request = urllib.request.Request(  # noqa: S310 - fixed HTTPS API origin checked above.
        url,
        headers={
            "Authorization": "Bearer " + token,
            "Accept": "application/vnd.github+json",
            "User-Agent": "pr-feedback",
        },
    )
    try:
        with OPENER.open(request, timeout=60) as response:
            return read_json(response, url), response.headers
    except urllib.error.HTTPError as error:
        raise FeedbackError(f"HTTP {error.code} from {safe(url)}") from error
    except (urllib.error.URLError, OSError) as error:
        raise FeedbackError(f"request failed for {safe(url)}: {safe(error)}") from error


def get(path: str, requester: Requester = request_json) -> Record:
    data, _ = requester(API + path)
    if not isinstance(data, dict):
        raise FeedbackError(f"expected an object from {safe(path)}")
    return cast("Record", data)


def get_root(path: str, requester: Requester = request_json) -> Record:
    data, _ = requester(ROOT_API + path)
    if not isinstance(data, dict):
        raise FeedbackError(f"expected an object from {safe(path)}")
    return cast("Record", data)


def get_all(path: str, key: str | None = None, requester: Requester = request_json) -> list[Record]:
    url = API + path
    values: list[Record] = []
    seen: set[str] = set()
    while url and url not in seen:
        seen.add(url)
        data, headers = requester(url)
        page = (
            data
            if key is None
            else (cast("Record", data).get(key) if isinstance(data, dict) else None)
        )
        if not isinstance(page, list):
            raise FeedbackError(f"expected a list page from {safe(url)}")
        for entry in cast("list[object]", page):
            if not isinstance(entry, dict):
                raise FeedbackError(f"expected object entries from {safe(url)}")
            values.append(cast("Record", entry))
        page_url = url
        url = ""
        for link in headers.get("Link", "").split(","):
            if 'rel="next"' not in link:
                continue
            match = re.search(r"<([^>]+)>", link)
            if not match:
                raise FeedbackError(f"malformed next-page link from {safe(page_url)}")
            url = match.group(1)
            if not url.startswith(ROOT_API + "/"):
                raise FeedbackError("refusing next-page URL outside api.github.com")
            break
    if url in seen:
        raise FeedbackError(f"pagination cycle at {safe(url)}")
    return values


def heading(text: str) -> None:
    print("")
    print("=" * 78)
    print(text)
    print("=" * 78)


def record(item: Record, key: str) -> Record:
    value = item.get(key)
    if value is None:
        return {}
    if not isinstance(value, dict) or not all(
        isinstance(key, str) for key in cast("Mapping[object, object]", value)
    ):
        raise FeedbackError(f"expected an object in {key}")
    return cast("Record", value)


def text(item: Record, key: str) -> str:
    value = item.get(key)
    if value is None:
        return ""
    if not isinstance(value, str):
        raise FeedbackError(f"expected text in {key}")
    return value


def actor(item: Record) -> Record:
    return record(item, "user")


def is_trusted_automation(item: Record) -> bool:
    account = actor(item)
    login = text(account, "login").casefold()
    return account.get("type") == "Bot" and login in TRUSTED_AUTOMATION


def classify(item: Record, viewer_login: str) -> str:
    login = text(actor(item), "login")
    if viewer_login and login.casefold() == viewer_login.casefold():
        return "authenticated account"
    if login.casefold() in TRUSTED_AUTHORS:
        return "repository-trusted author"
    if is_trusted_automation(item):
        return "trusted deterministic automation"
    return "OUTSIDE OR UNKNOWN -- USER INPUT REQUIRED"


def identity(item: Record, viewer_login: str) -> str:
    account = actor(item)
    login = text(account, "login") or "unknown"
    association = text(item, "author_association") or "unknown"
    return f"{safe(login)} [{classify(item, viewer_login)}; association={safe(association)}]"


def collect_outside(
    pull: Record,
    commits: list[Record],
    inline: list[Record],
    reviews: list[Record],
    conversation: list[Record],
    viewer_login: str,
) -> list[tuple[str, str, str]]:
    outside: list[tuple[str, str, str]] = []

    def audit(kind: str, item: Record, url: str = "") -> None:
        if classify(item, viewer_login).startswith("OUTSIDE"):
            login = text(actor(item), "login") or "unknown"
            outside.append((kind, login, url))

    audit("pull request author", pull, text(pull, "html_url"))
    for commit in commits:
        audit("commit author", {"user": record(commit, "author")}, text(commit, "html_url"))
        audit("commit committer", {"user": record(commit, "committer")}, text(commit, "html_url"))
        message = text(record(commit, "commit"), "message")
        for name, email in re.findall(r"(?im)^Co-authored-by:\s*(.+?)\s*<([^>]+)>\s*$", message):
            outside.append(
                (
                    "commit co-author (GitHub login unresolved)",
                    f"{name} <{email}>",
                    text(commit, "html_url"),
                )
            )
    for comment in inline:
        audit("inline review comment", comment, text(comment, "html_url"))
    for review in reviews:
        audit("review submission", review, text(review, "html_url"))
    for comment in conversation:
        audit("conversation comment", comment, text(comment, "html_url"))
    return outside


def select_threads(comments: list[Record], root_comments_only: bool) -> list[Record]:
    if not root_comments_only:
        return list(comments)
    return [comment for comment in comments if comment.get("in_reply_to_id") is None]


def run_self_tests() -> None:
    passed = 0

    def expect(condition: bool, name: str) -> None:
        nonlocal passed
        if not condition:
            raise AssertionError(name)
        passed += 1

    first_url = API + "/fixture?per_page=100"
    second_url = API + "/fixture?page=2"
    pages: dict[str, tuple[object, Mapping[str, str]]] = {
        first_url: ([{"id": 1}], {"Link": f'<{second_url}>; rel="next"'}),
        second_url: ([{"id": 2}], {}),
    }

    def fixture_requester(url: str) -> tuple[object, Mapping[str, str]]:
        return pages[url]

    expect(
        [item["id"] for item in get_all("/fixture?per_page=100", requester=fixture_requester)]
        == [1, 2],
        "pagination",
    )
    self_item = {"user": {"login": "wallstop", "type": "User"}}
    outside_item = {"user": {"login": "contributor", "type": "User"}}
    unknown_item = {"user": None}
    trusted_bot = {"user": {"login": "github-actions[bot]", "type": "Bot"}}
    cursor_bugbot = {"user": {"login": "cursor[bot]", "type": "Bot"}}
    copilot_reviewer = {"user": {"login": "copilot-pull-request-reviewer[bot]", "type": "Bot"}}
    copilot_agent = {"user": {"login": "copilot-swe-agent[bot]", "type": "Bot"}}
    third_party_bot = {"user": {"login": "third-party[bot]", "type": "Bot"}}
    expect(classify(self_item, "wallstop") == "authenticated account", "self identity")
    expect(classify(self_item, "another-login") == "repository-trusted author", "trusted author")
    expect(classify(outside_item, "wallstop").startswith("OUTSIDE"), "outside identity")
    expect(classify(unknown_item, "wallstop").startswith("OUTSIDE"), "unknown identity")
    expect(classify(trusted_bot, "wallstop") == "trusted deterministic automation", "trusted bot")
    expect(
        classify(cursor_bugbot, "wallstop") == "trusted deterministic automation", "Cursor Bugbot"
    )
    expect(
        classify(copilot_reviewer, "wallstop") == "trusted deterministic automation",
        "Copilot reviewer",
    )
    expect(
        classify(copilot_agent, "wallstop") == "trusted deterministic automation", "Copilot agent"
    )
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

    def failing_requester(_url: str) -> tuple[object, Mapping[str, str]]:
        nonlocal calls
        calls += 1
        if calls == 1:
            return [{"id": 1}], {"Link": f'<{second_url}>; rel="next"'}
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
        get("/pulls/1", requester=lambda _url: ([], {}))
        expect(False, "pull shape failure")
    except FeedbackError:
        expect(True, "pull shape failure")
    try:
        get_root("/user", requester=lambda _url: ([], {}))
        expect(False, "viewer shape failure")
    except FeedbackError:
        expect(True, "viewer shape failure")

    json_calls = 0

    def invalid_json_page(url: str) -> tuple[object, Mapping[str, str]]:
        nonlocal json_calls
        json_calls += 1
        if json_calls == 1:
            return [{"id": 1}], {"Link": f'<{second_url}>; rel="next"'}
        read_json(io.StringIO("{"), url)
        raise FeedbackError("fixture invalid JSON unexpectedly accepted")

    try:
        get_all("/fixture?per_page=100", requester=invalid_json_page)
        expect(False, "pagination JSON failure")
    except FeedbackError:
        expect(True, "pagination JSON failure")
    malformed_url = API + "/malformed?per_page=100"
    try:
        get_all(
            "/malformed?per_page=100",
            requester=lambda _url: ([{"id": 1}], {"Link": 'not-a-url; rel="next"'}),
        )
        expect(False, "malformed pagination link")
    except FeedbackError as error:
        expect(
            str(error) == f"malformed next-page link from {malformed_url}",
            "malformed pagination link",
        )
    try:
        get_all(
            "/off-host?per_page=100",
            requester=lambda _url: (
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
            requester=lambda _url: (
                [{"id": 1}],
                {"Link": f'<{cycle_url}>; rel="next"'},
            ),
        )
        expect(False, "pagination cycle")
    except FeedbackError as error:
        expect(str(error) == f"pagination cycle at {cycle_url}", "pagination cycle")
    http_error = urllib.error.HTTPError(first_url, 503, "fixture", Message(), None)
    with unittest.mock.patch.object(OPENER, "open", side_effect=http_error):
        try:
            request_json(first_url)
            expect(False, "HTTP failure")
        except FeedbackError:
            expect(True, "HTTP failure")
    expect(safe("ok\x1b[31m\x07") == "ok\\x1b[31m\\x07", "terminal sanitization")
    print(f"pr-feedback self-tests passed: {passed}")


def report() -> int:

    try:
        pull = get(f"/pulls/{number}")
        viewer = get_root("/user")
        viewer_login = text(viewer, "login")
        if not viewer_login:
            raise FeedbackError("authenticated GitHub login is missing")
        head_sha = text(record(pull, "head"), "sha")
        if not head_sha:
            raise FeedbackError("pull request head SHA is missing")
        commits = get_all(f"/pulls/{number}/commits?per_page=100")
        inline = get_all(f"/pulls/{number}/comments?per_page=100")
        reviews = get_all(f"/pulls/{number}/reviews?per_page=100")
        conversation = get_all(f"/issues/{number}/comments?per_page=100")
        check_runs = get_all(f"/commits/{head_sha}/check-runs?per_page=100", "check_runs")
    except FeedbackError as error:
        print(f"pr-feedback: incomplete GitHub data: {safe(error)}", file=sys.stderr)
        return 4

    outside = collect_outside(pull, commits, inline, reviews, conversation, viewer_login)

    heading("AUTHORSHIP AUDIT  (login comparison; author_association is not identity)")
    print(f"  authenticated GitHub login: {safe(viewer_login)}")
    print(f"  pull request author: {identity(pull, viewer_login)}")
    print(f"  commits inspected: {len(commits)} (all pages)")
    if outside:
        print("")
        print(
            f"  USER INPUT REQUIRED BEFORE ACTION OR REPLY: {len(outside)} outside/unknown input(s)"
        )
        for kind, login, url in outside:
            print(f"    - {safe(kind)}: {safe(login)}  {safe(url)}")
    else:
        print("  outside/unknown human inputs: none")

    heading("1. INLINE REVIEW THREADS  (file + line; NOT returned by /issues/{n}/comments)")

    def print_thread(comment: Record) -> None:
        print("")
        print(
            "  #{}  {}  {}:{}".format(
                safe(comment.get("id", "?")),
                identity(comment, viewer_login),
                safe(comment.get("path", "?")),
                safe(comment.get("line") or comment.get("original_line") or "?"),
            )
        )
        if comment.get("in_reply_to_id") is not None:
            print("    (reply to #{})".format(safe(comment["in_reply_to_id"])))
        for line in text(comment, "body").splitlines():
            print("    " + safe(line))
        print("    {}".format(safe(text(comment, "html_url"))))

    # A review thread outside trusted automation is the highest-priority surface: trusted bots repost
    # stale findings on every push, but an outside or unknown account needs user input. Print those
    # first, newest author-side last so the freshest thread is adjacent to section 2, and say the counts out loud so
    # "no outside threads" is affirmative evidence rather than something a reader has to scroll to
    # disprove.
    threads = select_threads(inline, root_comments_only)
    outside_threads = [comment for comment in threads if not is_trusted_automation(comment)]
    automation_threads = [comment for comment in threads if is_trusted_automation(comment)]
    if not threads:
        print("  (none)")
    else:
        print(
            "  {} comment(s): {} outside/self/unknown ({}), {} from trusted automation".format(
                len(threads),
                len(outside_threads),
                ", ".join(
                    sorted(
                        {
                            safe(text(actor(comment), "login") or "unknown")
                            for comment in outside_threads
                        }
                    )
                )
                or "none",
                len(automation_threads),
            )
        )
    for comment in outside_threads + automation_threads:
        print_thread(comment)

    heading("2. REVIEW SUBMISSIONS  (approve / request-changes / comment bodies)")
    # Keyed by the bot's login with the "[bot]" suffix dropped, because that is what a check run
    # calls it: the Copilot reviewer runs under the github-actions app, so app.slug is "github-actions"
    # and only the run's NAME carries the bot's identity.
    reviews_by_bot: dict[str, list[Record]] = {}
    for review in reviews:
        login = text(record(review, "user"), "login").casefold()
        if is_trusted_automation(review):
            reviews_by_bot.setdefault(login[: -len("[bot]")], []).append(review)
    if not reviews:
        print("  (none)")
    for review in reviews:
        print("")
        print(
            "  {}  {}  commit {}".format(
                identity(review, viewer_login),
                safe(review.get("state", "")),
                safe(text(review, "commit_id")[:8] or "unknown"),
            )
        )
        body = text(review, "body").strip()
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
        print(f"  {identity(comment, viewer_login)}")
        for line in text(comment, "body").splitlines():
            print("    " + safe(line))

    heading(f"4. FAILING CHECK RUNS  (head {safe(head_sha[:8])})")
    failing = [
        run
        for run in check_runs
        if run.get("conclusion") not in ("success", "skipped", "neutral", None)
    ]
    if not failing:
        print("  (none)")
    for run in failing:
        print(
            "  {:<12} {}  {}".format(
                safe(run.get("conclusion")), safe(run.get("name")), safe(run.get("html_url", ""))
            )
        )
        # The Copilot reviewer reported "reached their quota limit" in its review body while its
        # job log named only the failing step, and nine runs were read as an entitlement fault
        # before anybody looked one endpoint over (#661). Print the reason beside the red.
        run_identity = "{} {}".format(text(run, "name"), text(record(run, "app"), "slug"))
        run_identity = run_identity.casefold()
        for bot, said_reviews in reviews_by_bot.items():
            if bot not in run_identity:
                continue
            # Reviews print unfiltered in section 2 (an empty body points at section 1), so the
            # helper must skip empties itself: grabbing [:1] can land on a bodyless review and
            # hide the quota/refusal reason a failing check's own log omits (#661).
            for review in [r for r in said_reviews if text(r, "body").strip()][:1]:
                said = [line for line in text(review, "body").splitlines() if line.strip()]
                if said:
                    print(
                        "               ^ {} said: {}".format(
                            safe(text(actor(review), "login") or "unknown"), safe(said[0].strip())
                        )
                    )

    print("")
    return 0


def bash_command() -> str | None:
    if sys.platform == "win32":
        git = shutil.which("git")
        if git is not None:
            for installation in Path(git).resolve().parents[:3]:
                for relative in ("bin/bash.exe", "usr/bin/bash.exe"):
                    candidate = installation / relative
                    if candidate.is_file():
                        return str(candidate)
    return shutil.which("bash")


def main(arguments: list[str] | None = None) -> int:
    global token, number, root_comments_only
    args = sys.argv[1:] if arguments is None else arguments
    if not args or args[0] in ("-h", "--help"):
        print(USAGE)
        return 0
    if args == ["--self-test"]:
        run_self_tests()
        return 0
    if "--unresolved-only" in args:
        print(
            "pr-feedback: --unresolved-only was misleading because REST comments do not expose thread resolution; use --root-comments-only",
            file=sys.stderr,
        )
        return 2
    if (
        not args[0].isascii()
        or not args[0].isdigit()
        or any(arg != "--root-comments-only" for arg in args[1:])
    ):
        print(
            "pr-feedback: expected a numeric pull request number and optional --root-comments-only",
            file=sys.stderr,
        )
        return 2
    number = args[0]
    root_comments_only = "--root-comments-only" in args[1:]
    bash = bash_command()
    if bash is None:
        print("pr-feedback: bash is required for the credential helper", file=sys.stderr)
        return 3
    try:
        credential = subprocess.run(  # noqa: S603 - fixed credential helper, no user arguments.
            [bash, str(Path(__file__).resolve().with_name("github-token.sh"))],
            capture_output=True,
            text=True,
            check=False,
            timeout=60,
        )
    except (OSError, subprocess.TimeoutExpired):
        print("pr-feedback: credential helper could not complete", file=sys.stderr)
        return 3
    if credential.returncode or not credential.stdout.strip():
        print("pr-feedback: no GitHub credential; see scripts/github-token.sh", file=sys.stderr)
        return 3
    token = credential.stdout.strip()
    try:
        return report()
    except FeedbackError as error:
        print(f"pr-feedback: incomplete GitHub data: {safe(error)}", file=sys.stderr)
        return 4


if __name__ == "__main__":
    sys.exit(main())
