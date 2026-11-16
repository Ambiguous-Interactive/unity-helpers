"""Admit opted-in releases and require exact-source native validation evidence."""

from __future__ import annotations

import argparse
import base64
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from collections.abc import Callable, Mapping
from email.message import Message
from io import TextIOWrapper
from pathlib import Path
from typing import IO, TYPE_CHECKING, Protocol, cast

if TYPE_CHECKING:
    from http.client import HTTPMessage

Record = Mapping[str, object]
Request = Callable[[str], object]
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+")
SHA = re.compile(r"[a-f0-9]{40}")
VERSION = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")
REQUIRED_WORKFLOWS = (
    "unity-tests.yml",
    "unity-compatibility.yml",
    "local-gates.yml",
    "repo-lint.yml",
)
INDEPENDENT_WORKFLOWS = (
    "build-publish-devcontainer.yml",
    "deploy-pages.yml",
    "deploy-wiki.yml",
    "sync-issue-template.yml",
)
MARKER = "<!-- unity-helpers:auto-publish -->"
REQUIRED_STEPS = (
    "Acquire organization Unity lock",
    "Export Unity package smoke artifact",
    "Return Unity license",
    "Classify Unity cleanup evidence",
    "Release organization Unity lock",
    "Require confirmed Unity cleanup",
)
ROOT = Path(__file__).resolve().parents[1]


def record(value: object) -> Record:
    return cast("Record", value) if isinstance(value, dict) else {}


def field(value: object, *keys: str) -> object:
    for key in keys:
        value = record(value).get(key)
    return value


def matches(pattern: re.Pattern[str], value: object) -> bool:
    return isinstance(value, str) and pattern.fullmatch(value) is not None


def safe_integer(value: object) -> bool:
    return type(value) is int and 0 < value <= 9007199254740991


def same_identity(value: object, expected: object) -> bool:
    return safe_integer(value) and value == expected


def validation_event(run: object) -> bool:
    return field(run, "event") == "push" or (
        field(run, "event") == "workflow_dispatch"
        and field(run, "path") == ".github/workflows/unity-compatibility.yml"
    )


def eligible(event: object, repository: str) -> bool:
    run = field(event, "workflow_run")
    return (
        not field(event, "repository", "fork")
        and field(event, "repository", "full_name") == repository
        and validation_event(run)
        and field(run, "head_branch") == field(event, "repository", "default_branch")
        and field(run, "head_repository", "full_name") == repository
        and field(run, "conclusion") in ("success", "skipped")
        and matches(SHA, field(run, "head_sha"))
    )


def release_candidate(
    event: object, pulls: list[object], version: object, repository: str
) -> dict[str, object] | None:
    if not eligible(event, repository) or not matches(VERSION, version):
        return None
    sha = field(event, "workflow_run", "head_sha")
    branch = field(event, "repository", "default_branch")
    candidates: list[object] = []
    for pull in pulls:
        body = field(pull, "body")
        if (
            field(pull, "merged_at")
            and field(pull, "merge_commit_sha") == sha
            and field(pull, "base", "ref") == branch
            and field(pull, "base", "repo", "full_name") == repository
            and field(pull, "head", "repo", "full_name") == repository
            and field(pull, "head", "ref") == f"release/{version}"
            and field(pull, "title") == f"release: {version}"
            and isinstance(body, str)
            and MARKER in re.split(r"\r?\n", body)
        ):
            candidates.append(pull)
    return (
        {"sha": sha, "version": version, "branch": branch, "pull": field(candidates[0], "number")}
        if len(candidates) == 1
        else None
    )


def validation_state(runs: list[object], sha: str, branch: str, repository: str) -> str:
    latest: dict[str, Record] = {}
    for value in runs:
        run = record(value)
        path = run.get("path")
        if (
            not validation_event(run)
            or run.get("head_sha") != sha
            or run.get("head_branch") != branch
            or field(run, "repository", "full_name") != repository
            or (
                isinstance(path, str)
                and path in {f".github/workflows/{file}" for file in INDEPENDENT_WORKFLOWS}
            )
        ):
            continue
        if not isinstance(path, str) or not safe_integer(run.get("id")):
            raise ValueError("Invalid validation workflow identity.")
        previous = latest.get(path)
        if previous is None or cast("int", previous["id"]) < cast("int", run["id"]):
            latest[path] = run
    required = [f".github/workflows/{file}" for file in REQUIRED_WORKFLOWS]
    if any(path not in latest for path in required):
        return "waiting"
    pending = False
    for run in latest.values():
        if run.get("status") != "completed":
            pending = True
        elif run.get("conclusion") not in ("success", "skipped"):
            return "failed"
    if any(latest[path].get("conclusion") == "skipped" for path in required):
        return "failed"
    return "waiting" if pending else "passed"


class Api(Protocol):
    def get(self, endpoint: str) -> object: ...
    def post(self, endpoint: str, body: Record) -> object: ...
    def pages(self, endpoint: str, key: str | None = None) -> list[object]: ...


def auto_publish(
    event: object,
    repository: str,
    api: Api,
    sleep: Callable[[float], object] = time.sleep,
    polls: int = 40,
) -> dict[str, object]:
    if not eligible(event, repository):
        return {"status": "ineligible"}
    sha = cast("str", field(event, "workflow_run", "head_sha"))
    pulls = api.pages(f"commits/{sha}/pulls")
    content = record(api.get(f"contents/package.json?ref={sha}"))
    if content.get("encoding") != "base64" or not isinstance(content.get("content"), str):
        raise ValueError("Missing package metadata.")
    package: object = json.loads(base64.b64decode(cast("str", content["content"])))
    version = field(package, "version")
    candidate = release_candidate(event, pulls, version, repository)
    if candidate is None:
        return {"status": "ineligible"}
    branch = cast("str", candidate["branch"])
    for poll in range(polls):
        runs = api.pages(f"actions/runs?head_sha={sha}", "workflow_runs")
        state = validation_state(runs, sha, branch, repository)
        if state == "failed":
            return {"status": "validation-failed", **candidate}
        if state == "passed":
            if any(field(release, "tag_name") == version for release in api.pages("releases")):
                return {"status": "already-published", **candidate}
            publish_runs = api.pages(
                "actions/workflows/release.yml/runs?event=workflow_dispatch", "workflow_runs"
            )
            title = f"Release {version} from {sha}"
            for run in publish_runs:
                if field(run, "display_title") == title:
                    return {"status": "already-dispatched", "runId": field(run, "id"), **candidate}
            claim_ref = f"refs/tags/release-publish-claims/{version}"
            try:
                claim = api.post("git/refs", {"ref": claim_ref, "sha": sha})
                if field(claim, "ref") != claim_ref or field(claim, "object", "sha") != sha:
                    raise ValueError("Publication claim does not match the validated source.")
            except urllib.error.HTTPError as error:
                if error.code not in (409, 422):
                    raise
                claim = api.get(f"git/ref/tags/release-publish-claims/{version}")
                if field(claim, "ref") != claim_ref or not matches(
                    SHA, field(claim, "object", "sha")
                ):
                    raise ValueError("Invalid existing publication claim.") from error
                return {
                    "status": "already-claimed",
                    "claimedSha": field(claim, "object", "sha"),
                    **candidate,
                }
            api.post(
                "actions/workflows/release.yml/dispatches",
                {
                    "ref": branch,
                    "inputs": {
                        "version": version,
                        "source_ref": sha,
                        "export_only": "false",
                        "allow_tag_recovery": "false",
                    },
                },
            )
            return {"status": "dispatched", **candidate}
        if poll + 1 < polls:
            sleep(15)
    return {"status": "waiting-for-validation", **candidate}


def collect_pages(request: Request, endpoint: str, key: str) -> list[object]:
    items: list[object] = []
    for page in range(1, 21):
        values = field(request(f"{endpoint}&per_page=100&page={page}"), key)
        if not isinstance(values, list):
            raise ValueError(f"Native Unity validation returned no {key} array.")
        items.extend(cast("list[object]", values))
        if len(cast("list[object]", values)) < 100:
            return items
    raise ValueError("Native Unity validation exceeded its pagination limit.")


def trusted_runs(runs: list[object], repository: str, sha: str, workflow: str) -> list[Record]:
    return sorted(
        (
            record(run)
            for run in runs
            if (
                field(run, "head_sha") == sha
                and field(run, "repository", "full_name") == repository
                and field(run, "head_repository", "full_name") == repository
                and field(run, "path") == f".github/workflows/{workflow}"
                and safe_integer(field(run, "id"))
                and safe_integer(field(run, "run_attempt"))
            )
        ),
        key=lambda run: cast("int", run["id"]),
        reverse=True,
    )


def require_native_export(repository: str, source_sha: str, request: Request) -> dict[str, object]:
    if not matches(REPOSITORY, repository):
        raise ValueError("A repository in owner/name form is required.")
    if not matches(SHA, source_sha):
        raise ValueError("A complete lowercase source commit SHA is required.")
    runs = collect_pages(
        request,
        f"/repos/{repository}/actions/workflows/unity-tests.yml/runs?head_sha={source_sha}",
        "workflow_runs",
    )
    candidates = trusted_runs(runs, repository, source_sha, "unity-tests.yml")
    if not candidates:
        raise ValueError("No trusted Unity Tests run validates the selected source commit.")
    run = candidates[0]
    jobs = collect_pages(
        request, f"/repos/{repository}/actions/runs/{run['id']}/jobs?filter=latest", "jobs"
    )
    matches_jobs = [
        record(job) for job in jobs if field(job, "name") == "Unity package export smoke"
    ]
    if len(matches_jobs) != 1:
        raise ValueError(
            "Exactly one native Unity export job is required in the latest run attempt."
        )
    job = matches_jobs[0]
    steps = job.get("steps")
    if (
        not safe_integer(job.get("id"))
        or not same_identity(job.get("run_id"), run["id"])
        or not same_identity(job.get("run_attempt"), run["run_attempt"])
        or job.get("status") != "completed"
        or job.get("conclusion") != "success"
        or not isinstance(steps, list)
    ):
        raise ValueError(
            "Native Unity export has not completed successfully in the latest run attempt."
        )
    previous = 0
    for name in REQUIRED_STEPS:
        selected = [
            record(step) for step in cast("list[object]", steps) if field(step, "name") == name
        ]
        step: Record = selected[0] if selected else {}
        number = step.get("number")
        if (
            len(selected) != 1
            or step.get("status") != "completed"
            or step.get("conclusion") != "success"
            or not safe_integer(number)
            or cast("int", number) <= previous
        ):
            raise ValueError(f"Native Unity export requires a successful ordered '{name}' step.")
        previous = cast("int", number)
    return {"runId": run["id"], "jobId": job["id"], "sourceSha": source_sha}


def require_compatibility(repository: str, source_sha: str, request: Request) -> int:
    if not matches(REPOSITORY, repository) or not matches(SHA, source_sha):
        raise ValueError(
            "A repository and complete source SHA are required for compatibility validation."
        )
    runs = collect_pages(
        request,
        f"/repos/{repository}/actions/workflows/unity-compatibility.yml/runs?head_sha={source_sha}",
        "workflow_runs",
    )
    candidates = [
        run
        for run in trusted_runs(runs, repository, source_sha, "unity-compatibility.yml")
        if run.get("event") in ("push", "workflow_dispatch") and run.get("head_branch") == "main"
    ]
    if (
        not candidates
        or candidates[0].get("status") != "completed"
        or candidates[0].get("conclusion") != "success"
    ):
        raise ValueError(
            "Older-editor compatibility has not completed successfully for the selected source commit."
        )
    run = candidates[0]
    jobs = collect_pages(
        request, f"/repos/{repository}/actions/runs/{run['id']}/jobs?filter=latest", "jobs"
    )
    versions: object = json.loads(
        (ROOT / ".github/unity-versions.json").read_text(encoding="utf-8")
    )
    all_versions = field(versions, "all")
    if not isinstance(all_versions, list) or len(cast("list[object]", all_versions)) < 2:
        raise ValueError("Compatibility requires the supported older-editor inventory.")
    expected = [
        f"Compatibility Unity {version} (selected modes)"
        for version in cast("list[object]", all_versions)[:-1]
    ]
    for name in [*expected, "Unity Compatibility Success"]:
        selected = [record(job) for job in jobs if field(job, "name") == name]
        job: Record = selected[0] if selected else {}
        if (
            len(selected) != 1
            or not same_identity(job.get("run_id"), run["id"])
            or not same_identity(job.get("run_attempt"), run["run_attempt"])
            or job.get("status") != "completed"
            or job.get("conclusion") != "success"
        ):
            raise ValueError(f"Compatibility requires one successful current-attempt '{name}' job.")
    return cast("int", run["id"])


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
        del req, code, msg, headers, newurl
        fp.close()
        raise ValueError("Refusing GitHub API redirect.")


class GitHubApi:
    def __init__(self, repository: str, token: str) -> None:
        if not matches(REPOSITORY, repository) or not token:
            raise ValueError("Repository and token are required.")
        self.base = f"https://api.github.com/repos/{repository}/"
        self.repository = repository
        self.token = token
        self.opener = urllib.request.build_opener(RejectRedirects())

    def request(self, endpoint: str, method: str = "GET", body: Record | None = None) -> object:
        # A socket timeout alone cannot bound DNS, connection and a trickling response together.
        # The owned child has no mutation retries and subprocess.run kills/reaps it on timeout.
        payload = {
            "repository": self.repository,
            "token": self.token,
            "endpoint": endpoint,
            "method": method,
            "body": body,
        }
        try:
            result = subprocess.run(  # noqa: S603 - fixed interpreter and owned worker, token only on stdin.
                [sys.executable, str(Path(__file__).resolve()), "--http-worker"],
                input=json.dumps(payload).encode("utf-8"),
                capture_output=True,
                timeout=30,
                check=False,
            )
        except subprocess.TimeoutExpired as error:
            raise ValueError(
                "GitHub API request exceeded its 30-second deadline; do not retry an uncertain mutation."
            ) from error
        if result.returncode:
            raise ValueError(
                "GitHub API request worker failed; do not retry an uncertain mutation."
            )
        response = record(cast("object", json.loads(result.stdout)))
        status = response.get("http_status")
        if type(status) is int and 100 <= status <= 599:
            raise urllib.error.HTTPError(
                self.base + endpoint, status, "GitHub API request failed", Message(), None
            )
        if isinstance(response.get("error"), str):
            raise ValueError(cast("str", response["error"]))
        if "value" not in response:
            raise ValueError("Invalid GitHub API request worker response.")
        return response["value"]

    def request_direct(
        self, endpoint: str, method: str = "GET", body: Record | None = None
    ) -> object:
        url = (
            f"https://api.github.com{endpoint}"
            if endpoint.startswith("/repos/")
            else self.base + endpoint
        )
        request = urllib.request.Request(  # noqa: S310 - fixed HTTPS API origin, redirects refused.
            url,
            method=method,
            data=json.dumps(body).encode("utf-8") if body is not None else None,
            headers={
                "Authorization": f"Bearer {self.token}",
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
                "Content-Type": "application/json",
            },
        )
        with self.opener.open(request, timeout=30) as response:
            return None if response.status == 204 else cast("object", json.load(response))

    def get(self, endpoint: str) -> object:
        return self.request(endpoint)

    def post(self, endpoint: str, body: Record) -> object:
        return self.request(endpoint, "POST", body)

    def pages(self, endpoint: str, key: str | None = None) -> list[object]:
        values: list[object] = []
        for page in range(1, 101):
            result = self.get(
                f"{endpoint}{'&' if '?' in endpoint else '?'}per_page=100&page={page}"
            )
            rows = field(result, key) if key is not None else result
            if not isinstance(rows, list):
                raise ValueError("Invalid paginated GitHub response.")
            values.extend(cast("list[object]", rows))
            if len(cast("list[object]", rows)) < 100:
                return values
        raise ValueError("GitHub pagination limit reached; refusing partial release evidence.")


def http_worker() -> int:
    try:
        payload = record(cast("object", json.load(sys.stdin)))
        repository, token, endpoint, method = (
            payload.get(key) for key in ("repository", "token", "endpoint", "method")
        )
        body = payload.get("body")
        if (
            not isinstance(repository, str)
            or not isinstance(token, str)
            or not isinstance(endpoint, str)
            or method not in ("GET", "POST")
            or (body is not None and not isinstance(body, dict))
        ):
            raise ValueError("Invalid GitHub API request worker input.")
        result = GitHubApi(repository, token).request_direct(
            endpoint, method, None if body is None else record(cast("object", body))
        )
        response: dict[str, object] = {"value": result}
    except urllib.error.HTTPError as error:
        response = {"http_status": error.code}
        error.close()
    except (OSError, ValueError) as error:
        response = {
            "error": f"GitHub API transport failed ({type(error).__name__}); do not retry an uncertain mutation."
        }
    print(json.dumps(response, separators=(",", ":")))
    return 0


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, TextIOWrapper):
            stream.reconfigure(encoding="utf-8", newline="\n", errors=stream.errors)
    if (sys.argv[1:] if argv is None else argv) == ["--http-worker"]:
        return http_worker()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("auto-publish", "require-native-export"))
    args = parser.parse_args(argv)
    repository = os.environ.get("GITHUB_REPOSITORY", "")
    try:
        if args.command == "auto-publish":
            event: object = json.loads(
                Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8")
            )
            result = auto_publish(
                event, repository, GitHubApi(repository, os.environ.get("GITHUB_TOKEN", ""))
            )
            print(json.dumps(result, separators=(",", ":")))
            summary = os.environ.get("GITHUB_STEP_SUMMARY")
            if summary:
                with Path(summary).open("a", encoding="utf-8", newline="\n") as stream:
                    stream.write(f"Release publication: {result['status']}\n")
        else:
            token = os.environ.get("GH_TOKEN", "")
            if not token:
                raise ValueError("GH_TOKEN is required to read native Unity validation metadata.")
            api = GitHubApi(repository, token)
            sha = os.environ.get("SOURCE_SHA", "")
            native = require_native_export(repository, sha, api.get)
            compatibility = require_compatibility(repository, sha, api.get)
            print(f"Confirmed older-editor compatibility in run {compatibility}.")
            print(
                f"Confirmed native Unity export and cleanup for {sha}: run {native['runId']}, job {native['jobId']}."
            )
    except (OSError, ValueError, KeyError) as error:
        print(f"::error::{error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
