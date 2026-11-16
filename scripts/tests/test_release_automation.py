"""Replay original release decisions and exercise publication without remote mutations."""

from __future__ import annotations

import base64
import copy
import io
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import urllib.error
import urllib.request
from email.message import Message
from http.client import HTTPMessage
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest
import yaml

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release_automation as release

if TYPE_CHECKING:
    from collections.abc import Mapping

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = release.record(
    json.loads((Path(__file__).parent / "fixtures/release-orchestration-v1.json").read_bytes())
)
REPOSITORY = cast("str", FIXTURE["repository"])
SHA = cast("str", FIXTURE["sha"])
VERSION = cast("str", FIXTURE["version"])
CASES = cast("list[dict[str, object]]", FIXTURE["cases"])


@pytest.mark.parametrize(
    "case", CASES, ids=lambda case: str(case["kind"]) + ":" + str(case["label"])
)
def test_original_decisions(case: dict[str, object]) -> None:
    kind = case["kind"]
    expected = case["result"]
    if kind == "candidate":
        actual = release.release_candidate(
            case["event"], cast("list[object]", case["pulls"]), case["version"], REPOSITORY
        )
        # Invalid JSON field types formerly threw in Node. They now fail admission closed.
        assert actual == (None if "error" in release.record(expected) else expected)
    elif kind == "state":
        rejection: ValueError | None = None
        actual_state: str | None = None
        try:
            actual_state = release.validation_state(
                cast("list[object]", case["runs"]), SHA, "main", REPOSITORY
            )
        except ValueError as error:
            rejection = error
        if rejection is not None:
            assert "workflow identity" in str(rejection)
            assert any(
                not release.safe_integer(release.field(run, "id"))
                or not isinstance(release.field(run, "path"), str)
                for run in cast("list[object]", case["runs"])
            )
        else:
            assert actual_state == expected
    else:

        def request(endpoint: str) -> object:
            return (
                {"workflow_runs": case["runs"]}
                if "/workflows/" in endpoint
                else {"jobs": case["jobs"]}
            )

        message = release.field(expected, "error")
        if isinstance(message, str):
            with pytest.raises(ValueError, match=re.escape(message)) as caught:
                release.require_native_export(REPOSITORY, SHA, request)
            assert str(caught.value) == message
        else:
            assert release.require_native_export(REPOSITORY, SHA, request) == expected


class FakeApi:
    def __init__(self) -> None:
        self.states = [copy.deepcopy(cast("list[object]", FIXTURE["runs"]))]
        self.releases: list[object] = []
        self.existing: list[object] = []
        self.posts: list[tuple[str, Mapping[str, object]]] = []
        self.reads = 0
        self.claim_error: Exception | None = None
        self.dispatch_error: Exception | None = None
        self.claimed_sha = SHA
        self.invalid_claim = False
        self.reserved = False
        self.lock = threading.Lock()

    def get(self, endpoint: str) -> object:
        if endpoint.startswith("git/ref/"):
            return (
                {}
                if self.invalid_claim
                else {
                    "ref": f"refs/tags/release-publish-claims/{VERSION}",
                    "object": {"sha": self.claimed_sha},
                }
            )
        assert endpoint == f"contents/package.json?ref={SHA}"
        return {
            "encoding": "base64",
            "content": base64.b64encode(json.dumps({"version": VERSION}).encode()).decode(),
        }

    def pages(self, endpoint: str, key: str | None = None) -> list[object]:
        if endpoint == f"commits/{SHA}/pulls":
            assert key is None
            return [FIXTURE["pull"]]
        if endpoint.startswith("actions/runs?"):
            assert key == "workflow_runs"
            state = self.states[min(self.reads, len(self.states) - 1)]
            self.reads += 1
            return state
        if endpoint == "releases":
            return self.releases
        assert endpoint.startswith("actions/workflows/release.yml/runs?")
        return self.existing

    def post(self, endpoint: str, body: Mapping[str, object]) -> object:
        with self.lock:
            self.posts.append((endpoint, body))
            if endpoint == "git/refs":
                if self.claim_error:
                    raise self.claim_error
                if self.reserved:
                    raise http_error(422)
                self.reserved = True
                return {"ref": body["ref"], "object": {"sha": body["sha"]}}
            if self.dispatch_error:
                raise self.dispatch_error
            return None


def http_error(status: int) -> urllib.error.HTTPError:
    return urllib.error.HTTPError(
        "https://api.github.com/fixture", status, "fixture", Message(), None
    )


def test_wait_then_claim_and_dispatch_exact_source() -> None:
    api = FakeApi()
    api.states.insert(0, [])
    sleeps: list[float] = []
    result = release.auto_publish(FIXTURE["event"], REPOSITORY, api, sleep=sleeps.append)
    assert result == {
        "status": "dispatched",
        "sha": SHA,
        "version": VERSION,
        "branch": "main",
        "pull": 42,
    }
    assert sleeps == [15]
    assert api.posts == [
        ("git/refs", {"ref": f"refs/tags/release-publish-claims/{VERSION}", "sha": SHA}),
        (
            "actions/workflows/release.yml/dispatches",
            {
                "ref": "main",
                "inputs": {
                    "version": VERSION,
                    "source_ref": SHA,
                    "export_only": "false",
                    "allow_tag_recovery": "false",
                },
            },
        ),
    ]


@pytest.mark.parametrize("mode", ["published", "dispatched", "failed", "waiting"])
def test_no_mutation_for_existing_failed_or_incomplete_evidence(mode: str) -> None:
    api = FakeApi()
    if mode == "published":
        api.releases = [{"tag_name": VERSION}]
    elif mode == "dispatched":
        api.existing = [
            {"id": 77, "display_title": f"Release {VERSION} from {SHA}", "conclusion": "failure"}
        ]
    elif mode == "failed":
        api.states[0] = [{**release.record(run), "conclusion": "failure"} for run in api.states[0]]
    else:
        api.states = [[]]
    sleeps: list[float] = []
    result = release.auto_publish(FIXTURE["event"], REPOSITORY, api, sleep=sleeps.append, polls=2)
    assert (
        result["status"]
        == {
            "published": "already-published",
            "dispatched": "already-dispatched",
            "failed": "validation-failed",
            "waiting": "waiting-for-validation",
        }[mode]
    )
    assert api.posts == []
    assert sleeps == ([15] if mode == "waiting" else [])


@pytest.mark.parametrize("status", [409, 422])
@pytest.mark.parametrize("claimed_sha", [SHA, "b" * 40])
def test_existing_claim_prevents_dispatch(status: int, claimed_sha: str) -> None:
    api = FakeApi()
    api.claim_error = http_error(status)
    api.claimed_sha = claimed_sha
    result = release.auto_publish(FIXTURE["event"], REPOSITORY, api)
    assert result["status"] == "already-claimed"
    assert result["claimedSha"] == claimed_sha
    assert len(api.posts) == 1


@pytest.mark.parametrize(
    "phase", ["claim-lost", "claim-forbidden", "dispatch-lost", "invalid-existing"]
)
def test_ambiguous_mutation_never_retried(phase: str) -> None:
    api = FakeApi()
    if phase == "claim-lost":
        api.claim_error = OSError("claim response lost")
    elif phase == "claim-forbidden":
        api.claim_error = http_error(403)
    elif phase == "dispatch-lost":
        api.dispatch_error = OSError("dispatch response lost")
    else:
        api.claim_error = http_error(422)
        api.invalid_claim = True
    with pytest.raises((OSError, ValueError)):
        release.auto_publish(FIXTURE["event"], REPOSITORY, api)
    assert len(api.posts) == (2 if phase == "dispatch-lost" else 1)


def test_atomic_claim_allows_only_one_racing_dispatch() -> None:
    from concurrent.futures import ThreadPoolExecutor

    api = FakeApi()

    def publish(_: int) -> dict[str, object]:
        return release.auto_publish(FIXTURE["event"], REPOSITORY, api)

    with ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(publish, range(2)))
    assert sorted(str(row["status"]) for row in results) == ["already-claimed", "dispatched"]
    assert sum(endpoint.endswith("dispatches") for endpoint, _ in api.posts) == 1


def test_ineligible_event_does_not_read_api() -> None:
    class RefuseApi(FakeApi):
        def pages(self, endpoint: str, key: str | None = None) -> list[object]:
            raise AssertionError((endpoint, key))

    event = copy.deepcopy(release.record(FIXTURE["event"]))
    event = {
        **event,
        "workflow_run": {**release.record(event["workflow_run"]), "event": "pull_request"},
    }
    assert release.auto_publish(event, REPOSITORY, RefuseApi()) == {"status": "ineligible"}


@pytest.mark.parametrize("flag", [{}, []], ids=["object", "array"])
def test_empty_container_fork_flag_is_ineligible_before_api_reads(flag: object) -> None:
    class RefuseApi(FakeApi):
        def pages(self, endpoint: str, key: str | None = None) -> list[object]:
            raise AssertionError((endpoint, key))

    event = {
        **release.record(FIXTURE["event"]),
        "repository": {
            **release.record(release.field(FIXTURE["event"], "repository")),
            "fork": flag,
        },
    }
    assert release.release_candidate(event, [FIXTURE["pull"]], VERSION, REPOSITORY) is None
    assert release.auto_publish(event, REPOSITORY, RefuseApi()) == {"status": "ineligible"}


@pytest.mark.parametrize(
    "data",
    [
        b'{"version":NaN}',
        '{"version":"1.2.3"}'.encode("utf-16"),
        b'\xef\xbb\xbf{"version":"1.2.3"}',
    ],
    ids=["non-json-number", "utf16", "bom"],
)
def test_package_metadata_retains_original_utf8_json_boundary(data: bytes) -> None:
    class PackageApi(FakeApi):
        def get(self, endpoint: str) -> object:
            assert endpoint.startswith("contents/package.json?")
            return {"encoding": "base64", "content": base64.b64encode(data).decode()}

    api = PackageApi()
    with pytest.raises(ValueError, match=r"JSON|UTF|utf|codec"):
        release.auto_publish(FIXTURE["event"], REPOSITORY, api)
    assert api.posts == []


@pytest.mark.parametrize("constant", ["NaN", "Infinity", "-Infinity"])
def test_non_json_event_number_refused_before_api(
    constant: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    event = tmp_path / "event.json"
    data = {
        **release.record(FIXTURE["event"]),
        "repository": {
            **release.record(release.field(FIXTURE["event"], "repository")),
            "fork": "NUMBER",
        },
    }
    event.write_text(json.dumps(data).replace('"NUMBER"', constant), encoding="utf-8")
    monkeypatch.setenv("GITHUB_EVENT_PATH", str(event))
    monkeypatch.setenv("GITHUB_REPOSITORY", REPOSITORY)
    monkeypatch.setenv("GITHUB_TOKEN", "fixture-token")
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", "")
    assert release.main(["auto-publish"]) == 1


@pytest.mark.parametrize(
    ("default_branch", "head_branch", "base_ref"),
    [({}, {}, {}), ([], [], []), (0, False, False)],
    ids=["objects", "arrays", "number-boolean"],
)
def test_malformed_matching_branch_fields_refused_before_api(
    default_branch: object,
    head_branch: object,
    base_ref: object,
) -> None:
    class RefuseApi(FakeApi):
        def pages(self, endpoint: str, key: str | None = None) -> list[object]:
            raise AssertionError((endpoint, key))

    event = {
        **release.record(FIXTURE["event"]),
        "repository": {
            **release.record(release.field(FIXTURE["event"], "repository")),
            "default_branch": default_branch,
        },
        "workflow_run": {
            **release.record(release.field(FIXTURE["event"], "workflow_run")),
            "head_branch": head_branch,
        },
    }
    pull = {
        **release.record(FIXTURE["pull"]),
        "base": {**release.record(release.field(FIXTURE["pull"], "base")), "ref": base_ref},
    }
    assert release.release_candidate(event, [pull], VERSION, REPOSITORY) is None
    assert release.auto_publish(event, REPOSITORY, RefuseApi()) == {"status": "ineligible"}


def compatibility_fixture() -> tuple[dict[str, object], list[object]]:
    run = {
        **release.record(FIXTURE["nativeRun"]),
        "event": "workflow_dispatch",
        "head_branch": "main",
        "status": "completed",
        "conclusion": "success",
        "path": ".github/workflows/unity-compatibility.yml",
    }
    versions = cast(
        "list[str]",
        release.field(json.loads((ROOT / ".github/unity-versions.json").read_bytes()), "all"),
    )
    names = [f"Compatibility Unity {version} (selected modes)" for version in versions[:-1]] + [
        "Unity Compatibility Success"
    ]
    jobs: list[object] = [
        {
            "name": name,
            "run_id": run["id"],
            "run_attempt": run["run_attempt"],
            "status": "completed",
            "conclusion": "success",
        }
        for name in names
    ]
    return run, jobs


@pytest.mark.parametrize(
    "mutation",
    ["none", "missing", "duplicate", "stale", "failure", "skipped", "cancelled", "queued"],
)
def test_every_older_editor_and_aggregate_required(mutation: str) -> None:
    run, jobs = compatibility_fixture()
    for index in range(len(jobs)):
        changed = copy.deepcopy(jobs)
        if mutation == "missing":
            changed.pop(index)
        elif mutation == "duplicate":
            changed.append(changed[index])
        elif mutation != "none":
            key, value = (
                ("run_attempt", 1)
                if mutation == "stale"
                else (("status", "queued") if mutation == "queued" else ("conclusion", mutation))
            )
            changed[index] = {**release.record(changed[index]), key: value}

        def request(endpoint: str, changed: list[object] = changed) -> object:
            return {"workflow_runs": [run]} if "/workflows/" in endpoint else {"jobs": changed}

        if mutation == "none":
            assert release.require_compatibility(REPOSITORY, SHA, request) == run["id"]
        else:
            with pytest.raises(ValueError, match="one successful current-attempt"):
                release.require_compatibility(REPOSITORY, SHA, request)


@pytest.mark.parametrize(
    "mutation", ["source", "branch", "event", "fork", "status", "conclusion", "newer-failure"]
)
def test_compatibility_requires_latest_trusted_run(mutation: str) -> None:
    run, jobs = compatibility_fixture()
    changes: dict[str, object] = {
        "source": {"head_sha": "b" * 40},
        "branch": {"head_branch": "feature"},
        "event": {"event": "pull_request"},
        "fork": {"head_repository": {"full_name": "outside/repo"}},
        "status": {"status": "in_progress"},
        "conclusion": {"conclusion": "failure"},
        "newer-failure": {"id": 101, "conclusion": "failure"},
    }
    runs = [{**run, **release.record(changes[mutation])}]
    if mutation == "newer-failure":
        runs.insert(0, run)

    def request(endpoint: str) -> object:
        return {"workflow_runs": runs} if "/workflows/" in endpoint else {"jobs": jobs}

    with pytest.raises(ValueError, match="not completed successfully"):
        release.require_compatibility(REPOSITORY, SHA, request)


@pytest.mark.parametrize("key", [None, "workflow_runs"])
@pytest.mark.parametrize("mode", ["two-pages", "missing-array", "exhausted"])
def test_api_pagination_never_accepts_partial_evidence(key: str | None, mode: str) -> None:
    class PagedApi(release.GitHubApi):
        calls = 0

        def get(self, endpoint: str) -> object:
            self.calls += 1
            assert endpoint == f"actions/runs?event=push&per_page=100&page={self.calls}"
            if mode == "missing-array":
                return {}
            rows: list[object] = [self.calls] * (
                100 if self.calls == 1 or mode == "exhausted" else 1
            )
            return rows if key is None else {key: rows}

    api = PagedApi(REPOSITORY, "fixture-token")
    if mode == "two-pages":
        assert api.pages("actions/runs?event=push", key) == [1] * 100 + [2]
        assert api.calls == 2
    else:
        with pytest.raises(ValueError, match=r"Invalid paginated|pagination limit"):
            api.pages("actions/runs?event=push", key)
        assert api.calls == (100 if mode == "exhausted" else 1)


@pytest.mark.parametrize("mode", ["two-pages", "missing-array", "exhausted"])
def test_native_pagination_bound_and_latest_second_page(mode: str) -> None:
    calls: list[str] = []
    run = release.record(FIXTURE["nativeRun"])

    def request(endpoint: str) -> object:
        calls.append(endpoint)
        if "/workflows/" not in endpoint:
            assert f"/runs/{run['id']}/jobs?filter=latest&per_page=100&page=1" in endpoint
            return {"jobs": [FIXTURE["nativeJob"]]}
        if mode == "missing-array":
            return {}
        if endpoint.endswith("&page=1") or mode == "exhausted":
            return {"workflow_runs": [{**run, "id": 99}] * 100}
        return {"workflow_runs": [run]}

    if mode == "two-pages":
        assert release.require_native_export(REPOSITORY, SHA, request) == {
            "runId": 100,
            "jobId": 200,
            "sourceSha": SHA,
        }
        assert len(calls) == 3
    else:
        with pytest.raises(ValueError, match=r"no workflow_runs array|pagination limit"):
            release.require_native_export(REPOSITORY, SHA, request)
        assert len(calls) == (20 if mode == "exhausted" else 1)


@pytest.mark.parametrize("value", ["", "short", "A" * 40, SHA + "\n"])
def test_invalid_source_refused_before_request(value: str) -> None:
    def request(endpoint: str) -> object:
        raise AssertionError(endpoint)

    with pytest.raises(ValueError, match="source commit SHA"):
        release.require_native_export(REPOSITORY, value, request)
    with pytest.raises(ValueError, match="complete source SHA"):
        release.require_compatibility(REPOSITORY, value, request)


@pytest.mark.parametrize("identity", ["run_id", "run_attempt"])
@pytest.mark.parametrize("variant", ["native", "compatibility"])
def test_boolean_is_not_a_numeric_run_identity(identity: str, variant: str) -> None:
    if variant == "native":
        run = {**release.record(FIXTURE["nativeRun"]), "id": 1, "run_attempt": 1}
        jobs: list[object] = [
            {**release.record(FIXTURE["nativeJob"]), "run_id": 1, "run_attempt": 1, identity: True}
        ]
    else:
        run, original = compatibility_fixture()
        run = {**run, "id": 1, "run_attempt": 1}
        jobs = [{**release.record(job), "run_id": 1, "run_attempt": 1} for job in original]
        jobs[0] = {**release.record(jobs[0]), identity: True}

    def request(endpoint: str) -> object:
        return {"workflow_runs": [run]} if "/workflows/" in endpoint else {"jobs": jobs}

    validate = (
        release.require_native_export if variant == "native" else release.require_compatibility
    )
    with pytest.raises(
        ValueError, match=r"has not completed successfully|one successful current-attempt"
    ):
        validate(REPOSITORY, SHA, request)


@pytest.mark.parametrize("repository", ["", "outside/../../repo", "owner\n/repo", "owner/repo\n"])
def test_invalid_repository_refused(repository: str) -> None:
    with pytest.raises(ValueError, match="Repository and token"):
        release.GitHubApi(repository, "fixture-token")
    with pytest.raises(ValueError, match="owner/name"):
        release.require_native_export(repository, SHA, lambda _: None)


@pytest.mark.parametrize(("method", "status"), [("GET", 200), ("POST", 204), ("GET", 403)])
def test_http_transport_literal_request_and_no_retry(
    method: str, status: int, monkeypatch: pytest.MonkeyPatch
) -> None:
    class Response(io.BytesIO):
        def __init__(self) -> None:
            super().__init__(b'{"fixture":true}')
            self.status = status

    calls: list[str] = []

    def open_request(request: urllib.request.Request, timeout: float) -> Response:
        calls.append(request.full_url)
        assert request.full_url == "https://api.github.com/repos/owner/package/releases"
        assert request.get_method() == method
        assert request.get_header("Authorization") == "Bearer fixture-token"
        assert request.get_header("X-github-api-version") == "2022-11-28"
        assert timeout == 30
        assert request.data is None or isinstance(request.data, bytes)
        assert (json.loads(request.data) if request.data is not None else None) == (
            {"name": "fixture"} if method == "POST" else None
        )
        if status == 403:
            raise http_error(403)
        return Response()

    api = release.GitHubApi(REPOSITORY, "fixture-token")
    monkeypatch.setattr(api.opener, "open", open_request)
    if status == 403:
        with pytest.raises(urllib.error.HTTPError, match="403"):
            api.request_direct("releases")
    elif method == "POST":
        assert api.request_direct("releases", "POST", {"name": "fixture"}) is None
    else:
        assert api.request_direct("releases") == {"fixture": True}
    assert len(calls) == 1


def test_redirects_refused_without_followup_request() -> None:
    response = io.BytesIO(b"redirect")
    with pytest.raises(ValueError, match="Refusing GitHub API redirect"):
        release.RejectRedirects().redirect_request(
            urllib.request.Request("https://api.github.com/fixture"),
            response,
            302,
            "redirect",
            HTTPMessage(),
            "https://outside.invalid/",
        )
    assert response.closed


def test_http_request_has_wall_clock_deadline_without_token_in_arguments(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    calls: list[list[str]] = []

    def run(command: list[str], **kwargs: object) -> subprocess.CompletedProcess[bytes]:
        calls.append(command)
        assert "fixture-token" not in repr(command)
        assert kwargs["capture_output"] is True
        assert kwargs["check"] is False
        assert kwargs["timeout"] == 30
        data = kwargs["input"]
        assert isinstance(data, bytes)
        payload = release.record(json.loads(data))
        assert payload["token"] == "fixture-token"  # noqa: S105 - deterministic fake token.
        assert payload["endpoint"] == "releases"
        raise subprocess.TimeoutExpired(command, 30)

    monkeypatch.setattr(subprocess, "run", run)
    with pytest.raises(ValueError, match="30-second deadline"):
        release.GitHubApi(REPOSITORY, "fixture-token").get("releases")
    assert len(calls) == 1


@pytest.mark.parametrize(
    "response",
    [
        {"value": None},
        {"value": {"fixture": True}},
        {"http_status": 422},
        {"error": "Fixture transport failure"},
        {},
        {"http_status": True},
    ],
)
def test_http_worker_response_and_http_error_status(
    response: dict[str, object],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    calls: list[list[str]] = []

    def run(command: list[str], **kwargs: object) -> subprocess.CompletedProcess[bytes]:
        calls.append(command)
        assert kwargs["timeout"] == 30
        return subprocess.CompletedProcess(command, 0, json.dumps(response).encode(), b"")

    monkeypatch.setattr(subprocess, "run", run)
    api = release.GitHubApi(REPOSITORY, "fixture-token")
    if "value" in response:
        assert api.get("releases") == response["value"]
    elif response.get("http_status") == 422:
        with pytest.raises(urllib.error.HTTPError, match="422"):
            api.get("releases")
    else:
        with pytest.raises(ValueError, match=r"Fixture transport failure|Invalid GitHub API"):
            api.get("releases")
    assert len(calls) == 1


@pytest.mark.parametrize("mode", ["success", "http-error", "transport-error", "invalid-input"])
def test_worker_envelope_without_network_or_secret_diagnostics(
    mode: str,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    data = {
        "repository": REPOSITORY,
        "token": "fixture-token",
        "endpoint": "releases",
        "method": "GET",
        "body": None,
    }
    if mode == "invalid-input":
        data["method"] = "DELETE"

    def request(
        api: release.GitHubApi, endpoint: str, method: str, body: release.Record | None
    ) -> object:
        assert api.repository == REPOSITORY
        assert endpoint == "releases"
        assert method == "GET"
        assert body is None
        if mode == "http-error":
            raise http_error(403)
        if mode == "transport-error":
            raise ValueError("secret must not escape: fixture-token")
        return {"fixture": True}

    monkeypatch.setattr(sys, "stdin", io.StringIO(json.dumps(data)))
    monkeypatch.setattr(release.GitHubApi, "request_direct", request)
    assert release.main(["--http-worker"]) == 0
    output = capsys.readouterr()
    assert "fixture-token" not in output.out + output.err
    result = release.record(json.loads(output.out))
    if mode == "success":
        assert result == {"value": {"fixture": True}}
    elif mode == "http-error":
        assert result == {"http_status": 403}
    else:
        assert "GitHub API transport failed" in str(result["error"])


def test_real_worker_cli_refuses_invalid_repository_without_network(tmp_path: Path) -> None:
    data = {
        "repository": "invalid",
        "token": "fixture-token",
        "endpoint": "releases",
        "method": "GET",
        "body": None,
    }
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/release_automation.py"), "--http-worker"],
        input=json.dumps(data).encode(),
        cwd=tmp_path,
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert result.returncode == 0
    assert "GitHub API transport failed" in str(release.field(json.loads(result.stdout), "error"))
    assert b"fixture-token" not in result.stdout + result.stderr


def workflow(file: str) -> release.Record:
    return release.record(
        yaml.load(
            (ROOT / ".github/workflows" / file).read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,  # noqa: S506 - BaseLoader constructs only strings and collections.
        )
    )


def test_workflow_permissions_checkout_concurrency_and_all_push_wakeups() -> None:
    automation = workflow("release-auto-publish.yml")
    triggers = release.record(automation["on"])
    assert release.field(triggers, "workflow_run", "types") == ["completed"]
    files = sorted((ROOT / ".github/workflows").glob("*.yml"))
    push_workflows = [
        workflow(file.name)
        for file in files
        if "push" in release.record(workflow(file.name).get("on"))
    ]
    assert sorted(
        cast("list[str]", release.field(triggers, "workflow_run", "workflows"))
    ) == sorted(str(entry["name"]) for entry in push_workflows)
    publish = release.field(automation, "jobs", "publish")
    assert release.field(publish, "permissions") == {
        "contents": "write",
        "pull-requests": "read",
        "actions": "write",
    }
    assert automation["concurrency"] == {
        "group": "release-auto-publish-${{ github.event.workflow_run.head_sha }}",
        "cancel-in-progress": "false",
    }
    steps = cast("list[object]", release.field(publish, "steps"))
    checkout = next(
        step for step in steps if str(release.field(step, "uses")).startswith("actions/checkout@")
    )
    assert release.field(checkout, "with", "ref") == "${{ github.event.repository.default_branch }}"
    assert release.field(checkout, "with", "persist-credentials") == "false"
    setup_index = next(
        index
        for index, step in enumerate(steps)
        if release.field(step, "uses") == "./.github/actions/setup-python-tools"
    )
    execute_index = next(
        index
        for index, step in enumerate(steps)
        if "release_automation.py auto-publish" in str(release.field(step, "run"))
    )
    assert setup_index < execute_index
    for file in files:
        if file.name in release.INDEPENDENT_WORKFLOWS:
            continue
        current = workflow(file.name)
        if "push" not in release.record(current.get("on")):
            continue
        group = str(release.field(current, "concurrency", "group"))
        assert (
            "github.sha"
            if file.name == "unity-compatibility.yml"
            else "github.event_name == 'push' && github.sha"
        ) in group
        native = file.name in {"unity-tests.yml", "unity-compatibility.yml"}
        assert release.field(current, "concurrency", "cancel-in-progress") == (
            "true" if native else "${{ github.event_name == 'pull_request' }}"
        )
        if native:
            preflight = release.field(current, "jobs", "runner-preflight", "concurrency")
            expected = (
                "${{ github.workflow }}-runner-preflight-${{ github.sha }}"
                if file.name == "unity-compatibility.yml"
                else "github.event_name == 'push' && github.sha"
            )
            assert expected in str(release.field(preflight, "group"))
            assert release.field(preflight, "cancel-in-progress") == "true"


def test_native_release_gate_and_publish_dependencies_remain_independent() -> None:
    current = workflow("release.yml")
    jobs = release.field(current, "jobs")
    native = release.field(jobs, "native-validation")
    assert release.field(native, "if") == "${{ !inputs.export_only }}"
    assert release.field(native, "permissions") == {"contents": "read", "actions": "read"}
    steps = cast("list[object]", release.field(native, "steps"))
    checkout = next(
        step for step in steps if str(release.field(step, "uses")).startswith("actions/checkout@")
    )
    assert release.field(checkout, "with", "ref") == "${{ github.workflow_sha }}"
    assert release.field(checkout, "with", "persist-credentials") == "false"
    gate = next(
        step
        for step in steps
        if "release_automation.py require-native-export" in str(release.field(step, "run"))
    )
    assert release.field(gate, "env", "SOURCE_SHA") == "${{ needs.verify-tag.outputs.source-sha }}"
    for name in ("prepare-tag", "publish"):
        job = release.field(jobs, name)
        assert "native-validation" in cast("list[str]", release.field(job, "needs"))
        assert "needs.native-validation.result == 'success'" in str(release.field(job, "if"))
    assert "inputs.export_only && 'Export' || 'Release'" in str(current["run-name"])


def test_prepare_opt_in_and_python_setup_before_release_lints() -> None:
    prepare = workflow("release-prepare.yml")
    assert (
        release.field(prepare, "on", "workflow_dispatch", "inputs", "auto_publish", "default")
        == "true"
    )
    prepare_job = release.field(prepare, "jobs", "prepare")
    for job in (prepare_job, release.field(workflow("release.yml"), "jobs", "validate-package")):
        steps = cast("list[object]", release.field(job, "steps"))
        setup = next(
            index
            for index, step in enumerate(steps)
            if release.field(step, "uses") == "./.github/actions/setup-python-tools"
        )
        validation = next(
            index
            for index, step in enumerate(steps)
            if "npm run lint:changelog" in str(release.field(step, "run"))
        )
        assert setup < validation
        assert release.field(steps[setup], "if") is None
    assert any(
        release.MARKER in str(release.field(step, "run"))
        and release.field(step, "env", "AUTO_PUBLISH") == "${{ inputs.auto_publish }}"
        for step in cast("list[object]", release.field(prepare_job, "steps"))
    )


@pytest.mark.parametrize("launcher", ["direct", "scripts/release-tools/auto-publish-release.js"])
@pytest.mark.parametrize("relative", [False, True])
def test_ineligible_cli_and_summary_from_foreign_directory(
    launcher: str, tmp_path: Path, relative: bool
) -> None:
    event = tmp_path / "event.json"
    event.write_text('{"workflow_run":{"event":"pull_request"}}', encoding="utf-8")
    summary = tmp_path / "summary.md"
    if launcher == "direct":
        command = [sys.executable, str(ROOT / "scripts/release_automation.py"), "auto-publish"]
    else:
        node = shutil.which("node")
        assert node is not None
        command = [node, str(ROOT / launcher)]
    result = subprocess.run(
        command,
        cwd=tmp_path,
        check=False,
        capture_output=True,
        timeout=30,
        env={
            **os.environ,
            "GITHUB_EVENT_PATH": event.name if relative else str(event),
            "GITHUB_REPOSITORY": REPOSITORY,
            "GITHUB_TOKEN": "fixture-token",
            "GITHUB_STEP_SUMMARY": summary.name if relative else str(summary),
        },
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert json.loads(result.stdout) == {"status": "ineligible"}
    assert result.stdout == b'{"status":"ineligible"}\n'
    assert summary.read_bytes() == b"Release publication: ineligible\n"


def test_cli_stdout_preserves_lf_on_translating_stream(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    event = tmp_path / "event.json"
    event.write_text('{"workflow_run":{"event":"pull_request"}}', encoding="utf-8")
    monkeypatch.setenv("GITHUB_EVENT_PATH", str(event))
    monkeypatch.setenv("GITHUB_REPOSITORY", REPOSITORY)
    monkeypatch.setenv("GITHUB_TOKEN", "fixture-token")
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", "")
    data = io.BytesIO()
    stream = io.TextIOWrapper(data, encoding="utf-8", newline="\r\n")
    monkeypatch.setattr(sys, "stdout", stream)
    assert release.main(["auto-publish"]) == 0
    stream.flush()
    assert data.getvalue() == b'{"status":"ineligible"}\n'


@pytest.mark.parametrize(
    "launcher", ["direct", "scripts/release-tools/require-native-unity-export.js"]
)
def test_missing_native_token_cli_refuses_without_network(launcher: str, tmp_path: Path) -> None:
    if launcher == "direct":
        command = [
            sys.executable,
            str(ROOT / "scripts/release_automation.py"),
            "require-native-export",
        ]
    else:
        node = shutil.which("node")
        assert node is not None
        command = [node, str(ROOT / launcher)]
    result = subprocess.run(
        command,
        cwd=tmp_path,
        check=False,
        capture_output=True,
        timeout=30,
        env={**os.environ, "GH_TOKEN": "", "GITHUB_REPOSITORY": REPOSITORY, "SOURCE_SHA": SHA},
    )
    assert result.returncode == 1
    assert b"GH_TOKEN is required" in result.stderr


@pytest.mark.parametrize(
    "launcher",
    [
        "scripts/tests/test-auto-publish-release.js",
        "scripts/tests/test-require-native-unity-export.js",
    ],
)
def test_legacy_test_launcher_reaches_typed_control(launcher: str, tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [
            node,
            str(ROOT / launcher),
            "-k",
            "test_redirects_refused_without_followup_request",
        ],
        cwd=tmp_path,
        check=False,
        capture_output=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"1 passed" in result.stdout
