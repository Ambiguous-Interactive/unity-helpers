"""Exercise feedback provenance, pagination, output, and credential boundaries offline."""

from __future__ import annotations

import http.server
import io
import shutil
import subprocess
import sys
import threading
from typing import TYPE_CHECKING, ClassVar

if TYPE_CHECKING:
    import urllib.request
    from collections.abc import Mapping
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pr_feedback as feedback


def account(login: str, kind: str = "User") -> feedback.Record:
    return {"user": {"login": login, "type": kind}}


@pytest.mark.parametrize("login", sorted(feedback.TRUSTED_AUTOMATION))
def test_exact_trusted_bot_accounts(login: str) -> None:
    assert feedback.classify(account(login, "Bot"), "viewer") == "trusted deterministic automation"
    assert (
        feedback.classify(account(login.upper(), "Bot"), "viewer")
        == "trusted deterministic automation"
    )
    assert feedback.classify(account(login), "viewer").startswith("OUTSIDE")


@pytest.mark.parametrize("login", ["another-owner", "coderabbitai", "third-party[bot]", ""])
@pytest.mark.parametrize("kind", ["Bot", "User"])
def test_unknown_accounts_stay_outside(login: str, kind: str) -> None:
    item = {**account(login, kind), "author_association": "OWNER"}
    assert feedback.classify(item, "viewer").startswith("OUTSIDE")


def test_original_offline_controls(capsys: pytest.CaptureFixture[str]) -> None:
    feedback.run_self_tests()
    assert "self-tests passed: 22" in capsys.readouterr().out


@pytest.mark.parametrize("value", [1, True, [], "wrong"])
def test_malformed_nested_account_is_rejected(value: object) -> None:
    with pytest.raises(feedback.FeedbackError, match="expected an object"):
        feedback.classify({"user": value}, "viewer")


@pytest.mark.parametrize("field", ["login", "type"])
def test_malformed_account_text_is_rejected(field: str) -> None:
    item: feedback.Record = {"user": {"login": "someone", "type": "User", field: []}}
    if field == "login":
        with pytest.raises(feedback.FeedbackError, match="expected text"):
            feedback.classify(item, "viewer")
    else:
        assert feedback.classify(item, "viewer").startswith("OUTSIDE")


@pytest.mark.parametrize("page", [None, {}, "wrong", [1], [None], [[]]])
def test_malformed_page_is_rejected(page: object) -> None:
    def requester(_url: str) -> tuple[object, Mapping[str, str]]:
        return page, {}

    with pytest.raises(feedback.FeedbackError, match="expected"):
        feedback.get_all("/fixture", requester=requester)


def dataset() -> dict[str, object]:
    bot = account("coderabbitai[bot]", "Bot")
    outside = account("contributor")
    return {
        "/pulls/42": {**account("wallstop"), "head": {"sha": "abcdef1234"}},
        "/user": {"login": "wallstop"},
        "/pulls/42/commits?per_page=100": [
            {
                "author": {"login": "wallstop"},
                "committer": {"login": "wallstop"},
                "commit": {"message": "Subject"},
            }
        ],
        "/pulls/42/comments?per_page=100": [
            {**bot, "id": 10, "body": "BOT INLINE", "path": "Code.cs", "line": 12},
            {**outside, "id": 11, "body": "HUMAN INLINE\x1b[31m"},
            {**bot, "id": 12, "in_reply_to_id": 10, "body": "BOT REPLY"},
        ],
        "/pulls/42/reviews?per_page=100": [
            {**bot, "state": "APPROVED", "body": "", "commit_id": "abcdef1234"},
            {**bot, "state": "COMMENTED", "body": "quota reached"},
        ],
        "/issues/42/comments?per_page=100": [{**bot, "body": "CONVERSATION"}],
        "/commits/abcdef1234/check-runs?per_page=100": {
            "check_runs": [
                {"name": "coderabbitai", "conclusion": "failure"},
                {"name": "GREEN", "conclusion": "success"},
            ]
        },
    }


def render(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    *,
    roots_only: bool = False,
    data: dict[str, object] | None = None,
) -> tuple[int, str, list[str]]:
    fixtures = dataset() if data is None else data
    calls: list[str] = []

    class Response(io.StringIO):
        headers: ClassVar[dict[str, str]] = {}

    def urlopen(request: urllib.request.Request, *, timeout: int) -> Response:
        import json

        assert timeout == 60
        assert request.get_header("Authorization") == "Bearer fixture-token"
        url = request.full_url
        path = (
            url.removeprefix(feedback.API)
            if url.startswith(feedback.API)
            else url.removeprefix(feedback.ROOT_API)
        )
        calls.append(path)
        return Response(json.dumps(fixtures[path]))

    monkeypatch.setattr(feedback.OPENER, "open", urlopen)
    monkeypatch.setattr(feedback, "token", "fixture-token")
    monkeypatch.setattr(feedback, "number", "42")
    monkeypatch.setattr(feedback, "root_comments_only", roots_only)
    result = feedback.report()
    capture = capsys.readouterr()
    output = capture.out + capture.err
    assert "fixture-token" not in output
    return result, output, calls


def test_all_surfaces_and_coderabbit_grouping(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    result, output, calls = render(monkeypatch, capsys)
    assert result == 0
    assert len(calls) == 7
    assert "1 outside/unknown input(s)" in output
    assert output.index("HUMAN INLINE") < output.index("BOT INLINE") < output.index("BOT REPLY")
    assert "3 comment(s): 1 outside/self/unknown (contributor), 2 from trusted automation" in output
    assert "coderabbitai[bot] [trusted deterministic automation" in output
    assert "(no body -- inline comments only, see section 1)" in output
    assert "CONVERSATION" in output
    assert "^ coderabbitai[bot] said: quota reached" in output
    assert "GREEN" not in output
    assert "\\x1b[31m" in output
    for heading in ("AUTHORSHIP AUDIT", "1. INLINE", "2. REVIEW", "3. CONVERSATION", "4. FAILING"):
        assert heading in output


def test_root_filter_keeps_authorship_audit(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    result, output, _ = render(monkeypatch, capsys, roots_only=True)
    assert result == 0
    assert "BOT REPLY" not in output
    assert "1 outside/unknown input(s)" in output


@pytest.mark.parametrize("missing", ["/user", "/pulls/42"])
def test_missing_identity_fails_closed(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str], missing: str
) -> None:
    data = dataset()
    data[missing] = {}
    result, output, _ = render(monkeypatch, capsys, data=data)
    assert result == 4
    assert "AUTHORSHIP AUDIT" not in output
    assert "missing" in output


@pytest.mark.parametrize(
    "arguments",
    [["abc"], ["42", "--unknown"], ["--self-test", "42"], ["42", "--unresolved-only"], ["٤٢"]],
)
def test_invalid_cli_does_not_read_credentials(
    arguments: list[str], monkeypatch: pytest.MonkeyPatch
) -> None:
    def forbidden(*_args: object, **_kwargs: object) -> object:
        pytest.fail("invalid CLI must not invoke credential helper")

    monkeypatch.setattr(subprocess, "run", forbidden)
    assert feedback.main(arguments) == 2


@pytest.mark.parametrize("arguments", [[], ["--help"], ["-h"]])
def test_help(arguments: list[str], capsys: pytest.CaptureFixture[str]) -> None:
    assert feedback.main(arguments) == 0
    assert "Usage:" in capsys.readouterr().out


def test_credential_failure_never_prints_token(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    def run(*_args: object, **_kwargs: object) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess([], 3, "secret-token", "secret-token")

    monkeypatch.setattr(subprocess, "run", run)
    assert feedback.main(["42"]) == 3
    output = capsys.readouterr()
    assert "secret-token" not in output.out + output.err


def test_launcher_from_other_directory(tmp_path: Path) -> None:
    launcher = Path(__file__).resolve().parents[1] / "pr-feedback.sh"
    bash = feedback.bash_command()
    assert bash is not None
    result = subprocess.run(
        [bash, str(launcher), "--self-test"],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        timeout=60,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "self-tests passed: 22" in result.stdout


@pytest.mark.parametrize(
    "exception", [OSError("cannot spawn"), subprocess.TimeoutExpired("credential", 60)]
)
def test_credential_helper_failure_is_bounded(
    exception: Exception, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    def run(*_args: object, **_kwargs: object) -> subprocess.CompletedProcess[str]:
        raise exception

    monkeypatch.setattr(subprocess, "run", run)
    assert feedback.main(["42"]) == 3
    assert "credential helper could not complete" in capsys.readouterr().err


@pytest.mark.parametrize("relative", ["bin/bash.exe", "usr/bin/bash.exe"])
@pytest.mark.parametrize("git_directory", ["cmd", "bin", "mingw64/bin"])
def test_windows_uses_git_bash_before_the_wsl_stub(
    relative: str, git_directory: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    installation = tmp_path / "Git with spaces"
    git = installation / git_directory / "git.exe"
    git.parent.mkdir(parents=True)
    git.write_bytes(b"")
    bash = installation / relative
    bash.parent.mkdir(parents=True, exist_ok=True)
    bash.write_bytes(b"")
    calls: list[str] = []

    def which(command: str) -> str | None:
        calls.append(command)
        return str(git) if command == "git" else "wsl-stub"

    monkeypatch.setattr(sys, "platform", "win32")
    monkeypatch.setattr(shutil, "which", which)
    assert feedback.bash_command() == str(bash)
    assert calls == ["git"]


@pytest.mark.parametrize("status", [301, 302, 303, 307, 308])
@pytest.mark.parametrize("cross_origin", [False, True])
def test_real_redirect_never_receives_authorization(
    status: int, cross_origin: bool, monkeypatch: pytest.MonkeyPatch
) -> None:
    received: list[tuple[str, str]] = []

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            received.append((self.path, self.headers.get("Authorization", "")))
            if self.path == "/redirect":
                self.send_response(status)
                self.send_header("Location", target)
            else:
                self.send_response(200)
            self.end_headers()
            if self.path != "/redirect":
                self.wfile.write(b"{}")

    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    origin = f"http://127.0.0.1:{server.server_port}"
    target = f"http://localhost:{server.server_port}/sink" if cross_origin else origin + "/sink"
    monkeypatch.setattr(feedback, "ROOT_API", origin)
    monkeypatch.setattr(feedback, "token", "fixture-token")
    monkeypatch.setenv("no_proxy", "127.0.0.1,localhost")
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        with pytest.raises(feedback.FeedbackError, match=f"refusing HTTP {status} redirect"):
            feedback.request_json(origin + "/redirect")
        assert received == [("/redirect", "Bearer fixture-token")]
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)
    assert not thread.is_alive()
