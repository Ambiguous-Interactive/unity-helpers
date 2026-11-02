"""Offline cancellation-policy and complete watchdog scheduling controls."""

from __future__ import annotations

import datetime as dt
import importlib.util
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from typing import TYPE_CHECKING, Protocol, cast
from unittest.mock import patch

if TYPE_CHECKING:
    from collections.abc import Mapping
    from types import ModuleType

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "watchdog_cancel", ROOT / ".github/scripts/cancel-stuck-workflow.py"
)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Missing cancellation policy module")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class WatchdogPolicy(Protocol):
    OPERATOR_WORKFLOWS: frozenset[str]
    IDENTITY_FIELDS: tuple[str, ...]
    subprocess: ModuleType

    def eligible_run(
        self, run: object, expected: object, run_id: int, minimum_age: int, now: dt.datetime
    ) -> bool: ...
    def never_started_jobs(self, pages: object, run_id: int, *, run_attempt: int = 1) -> bool: ...
    def cancel_if_eligible(
        self,
        repository: str,
        run_id: int,
        expected: object,
        expected_jobs: object,
        minimum_age: int,
    ) -> bool: ...


GUARD = cast("WatchdogPolicy", MODULE)
NOW = dt.datetime.now(dt.UTC)
RUN_ID = 123


def run_metadata(
    path: str = ".github/workflows/build-publish-devcontainer.yml",
) -> dict[str, object]:
    return {
        "id": RUN_ID,
        "run_attempt": 1,
        "head_sha": "a" * 40,
        "workflow_id": 456,
        "path": path,
        "event": "pull_request",
        "head_branch": "test-branch",
        "html_url": "https://example.test/run/123",
        "status": "queued",
        "conclusion": None,
        "created_at": (NOW - dt.timedelta(hours=1))
        .isoformat(timespec="seconds")
        .replace("+00:00", "Z"),
    }


def queued_job(identifier: int = 1) -> dict[str, object]:
    created = (NOW - dt.timedelta(hours=1)).isoformat(timespec="seconds").replace("+00:00", "Z")
    return {
        "id": identifier,
        "run_id": RUN_ID,
        "run_attempt": 1,
        "created_at": created,
        "status": "queued",
        "conclusion": None,
        "started_at": created,
        "completed_at": None,
        "runner_id": 0,
        "runner_name": None,
        "steps": [],
        "labels": ["self-hosted", "Windows"],
    }


class CancellationPolicyTests(unittest.TestCase):
    def test_queued_creation_timestamp_does_not_mean_runner_assignment(self) -> None:
        job = queued_job()
        assert job["created_at"] == job["started_at"]
        assert GUARD.never_started_jobs([{"total_count": 1, "jobs": [job]}], RUN_ID)
        assert not GUARD.never_started_jobs(
            [{"total_count": 1, "jobs": [job]}], RUN_ID, run_attempt=2
        )

    def test_explicit_null_runner_id_is_unassigned_but_missing_is_unknown(self) -> None:
        job = {**queued_job(), "runner_id": None}
        assert GUARD.never_started_jobs([{"total_count": 1, "jobs": [job]}], RUN_ID)
        del job["runner_id"]
        assert not GUARD.never_started_jobs([{"total_count": 1, "jobs": [job]}], RUN_ID)

    def test_never_started_work_remains_eligible(self) -> None:
        run = run_metadata()
        assert GUARD.eligible_run(run, run, RUN_ID, 300, NOW)
        assert GUARD.never_started_jobs([{"total_count": 1, "jobs": [queued_job()]}], RUN_ID)

    def test_licensed_workflows_are_protected_before_any_assignment(self) -> None:
        for name in GUARD.OPERATOR_WORKFLOWS:
            for suffix in ("", "@refs/heads/main"):
                with self.subTest(name=name, suffix=suffix):
                    run = run_metadata(f".github/workflows/{name}{suffix}")
                    assert not GUARD.eligible_run(run, run, RUN_ID, 300, NOW)

    def test_changed_missing_and_malformed_run_state_is_refused(self) -> None:
        expected = run_metadata()
        for field in GUARD.IDENTITY_FIELDS:
            for value in (None, "changed", False):
                with self.subTest(field=field, value=value):
                    run = {**expected, field: value}
                    assert not GUARD.eligible_run(run, expected, RUN_ID, 300, NOW)
            run = dict(expected)
            del run[field]
            assert not GUARD.eligible_run(run, run, RUN_ID, 300, NOW)
        changes_cases: tuple[Mapping[str, object], ...] = (
            {"status": "in_progress"},
            {"status": "completed"},
            {"conclusion": "cancelled"},
            {"created_at": "bad"},
            {"created_at": NOW.isoformat()},
            {"created_at": "2026-01-01T00:00:00"},
            {"run_started_at": NOW.isoformat()},
            {"path": "unknown"},
        )
        for changes in changes_cases:
            with self.subTest(changes=changes):
                run = {**expected, **changes}
                assert not GUARD.eligible_run(run, run, RUN_ID, 300, NOW)

    def test_assigned_completed_and_incomplete_jobs_are_refused(self) -> None:
        changes_cases: tuple[Mapping[str, object], ...] = (
            {"status": "completed"},
            {"status": "in_progress"},
            {"status": "waiting"},
            {"runner_id": 42},
            {"runner_id": False},
            {"runner_name": "licensed-host"},
            {"started_at": NOW.isoformat()},
            {"completed_at": NOW.isoformat()},
            {"conclusion": "success"},
            {"steps": [{"conclusion": "success"}]},
            {"labels": []},
            {"labels": [None]},
            {"run_id": 456},
            {"id": True},
        )
        for changes in changes_cases:
            with self.subTest(changes=changes):
                assert not GUARD.never_started_jobs(
                    [{"total_count": 1, "jobs": [{**queued_job(), **changes}]}], RUN_ID
                )
        for field in queued_job():
            job = queued_job()
            del job[field]
            if field == "runner_name":
                continue
            with self.subTest(missing=field):
                assert not GUARD.never_started_jobs([{"total_count": 1, "jobs": [job]}], RUN_ID)

    def test_pagination_cannot_hide_progress_or_missing_jobs(self) -> None:
        jobs = [queued_job(i) for i in range(1, 102)]
        pages = [
            {"total_count": 101, "jobs": jobs[:100]},
            {"total_count": 101, "jobs": jobs[100:]},
        ]
        assert GUARD.never_started_jobs(pages, RUN_ID)
        assert not GUARD.never_started_jobs(pages[:1], RUN_ID)
        jobs[-1]["runner_id"] = 42
        assert not GUARD.never_started_jobs(pages, RUN_ID)
        bad_cases: tuple[object, ...] = (
            None,
            dict[str, object](),
            list[object](),
            [None],
            [{"jobs": list[object]()}],
            [{"total_count": 0, "jobs": list[object]()}],
            [{"total_count": 2, "jobs": [queued_job(), queued_job()]}],
        )
        for bad in bad_cases:
            assert not GUARD.never_started_jobs(bad, RUN_ID)

    def test_fresh_run_and_job_reads_precede_the_only_mutation(self) -> None:
        run = run_metadata()
        pages = [{"total_count": 1, "jobs": [queued_job()]}]
        for changed in (
            run,
            {**run, "run_attempt": 2},
            {**run, "status": "in_progress"},
        ):
            with (
                self.subTest(changed=changed),
                patch.object(GUARD, "read_api", side_effect=[run, pages, changed]) as api,
                patch.object(GUARD.subprocess, "run") as command,
            ):
                result = GUARD.cancel_if_eligible("owner/repo", RUN_ID, run, [queued_job()], 300)
                assert result == (changed == run)
                assert api.call_count == 3
                assert command.call_count == int(result)
                if result:
                    assert command.call_args.args[0] == [
                        "gh",
                        "run",
                        "cancel",
                        "123",
                        "--repo",
                        "owner/repo",
                    ]


FAKE_GH = r"""#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
args = sys.argv[1:]
fixture = json.loads(Path(os.environ["FIXTURE"]).read_text())
trace = Path(os.environ["TRACE"])
previous = trace.read_text().splitlines() if trace.exists() else []
with trace.open("a") as stream:
    stream.write(json.dumps(args) + "\n")
endpoint = args[-1]
if args[:2] == ["run", "cancel"]:
    if fixture.get("cancel_failure"):
        sys.exit(1)
    print("Cancellation requested")
elif "status=cancelled" in endpoint:
    print(json.dumps({"workflow_runs": []}))
elif "status=queued" in endpoint:
    print(json.dumps({"workflow_runs": [fixture["run"]]}))
elif "/actions/runners" in endpoint:
    print(json.dumps({"runners": [{"id": 1, "name": "idle", "status": "online", "busy": False, "labels": [{"name": "self-hosted"}, {"name": "Windows"}]}]}))
elif "/jobs?" in endpoint:
    reads = sum("/jobs?" in row for row in previous)
    jobs = fixture["jobs"] if reads == 0 else fixture.get("fresh_jobs", fixture["jobs"])
    result = {"total_count": len(jobs), "jobs": jobs}
    print(json.dumps([result] if "--slurp" in args else result))
elif endpoint.endswith("/actions/runs/123"):
    reads = sum(row.endswith('/actions/runs/123"]') for row in previous)
    if fixture.get("api_failure"):
        sys.exit(1)
    if fixture.get("invalid_json"):
        print("invalid")
    else:
        print(json.dumps(fixture.get("final_run" if reads else "fresh_run", fixture["run"])))
elif "/actions/workflows/" in endpoint:
    print(json.dumps({"path": ""}))
else:
    raise SystemExit("Unexpected gh request: " + repr(args))
"""

FAKE_GIT = r"""#!/usr/bin/env python3
import os, sys
from pathlib import Path
args = sys.argv[1:]
if "clone" in args:
    Path("repo").mkdir()
elif "ls-remote" in args:
    print("a" * 40 + " refs/heads/watchdog-state")
elif args[:1] == ["rev-parse"]:
    print("a" * 40)
"""


class WorkflowSchedulingTests(unittest.TestCase):
    workflow: str
    step: dict[str, object]

    @classmethod
    def setUpClass(cls) -> None:
        node = shutil.which("node")
        if node is None:
            raise RuntimeError("Node is required for workflow scheduling controls")
        cls.workflow = subprocess.check_output(
            [
                node,
                "-e",
                'const fs=require("fs"); const y=require("yaml"); const w=y.parse(fs.readFileSync(process.argv[1],"utf8")); process.stdout.write(JSON.stringify(w.jobs["audit-queue"].steps.find(s=>s.name==="Audit + cancel-and-redispatch")));',
                os.environ.get(
                    "WATCHDOG_WORKFLOW_PATH",
                    str(ROOT / ".github/workflows/stuck-job-watchdog.yml"),
                ),
            ],
            cwd=ROOT,
            text=True,
            timeout=30,
        )
        cls.step = json.loads(cls.workflow)

    def execute_workflow(
        self, fixture: Mapping[str, object], *, capped: bool = False, excluded: str = ""
    ) -> tuple[list[list[str]], list[list[str]], str]:
        with tempfile.TemporaryDirectory(prefix="watchdog-schedule-") as directory:
            temp = Path(directory)
            binaries = temp / "bin"
            binaries.mkdir()
            for name, body in (("gh", FAKE_GH), ("git", FAKE_GIT)):
                target = binaries / name
                target.write_text(body, encoding="utf-8")
                target.chmod(0o755)
            (temp / "fixture.json").write_text(json.dumps(fixture), encoding="utf-8")
            env = {
                **os.environ,
                **{
                    key: str(value)
                    for key, value in cast("dict[str, object]", self.step["env"]).items()
                },
                "PATH": str(binaries) + os.pathsep + os.environ["PATH"],
                "GH_TOKEN": "offline-test",
                "RUNNER_INVENTORY_TOKEN": "offline-test",
                "REPO": "owner/repo",
                "OWNER": "owner",
                "SELF_RUN_ID": "999",
                "GITHUB_WORKSPACE": str(ROOT),
                "RUNNER_TEMP": directory,
                "GITHUB_STEP_SUMMARY": str(temp / "summary"),
                "TRACE": str(temp / "trace"),
                "FIXTURE": str(temp / "fixture.json"),
                "EXTRA_EXCLUDED_WORKFLOWS": excluded,
            }
            if capped:
                env["MAX_CANCELS_PER_DAY"] = "0"
            result = subprocess.run(
                ["bash", "-c", cast("str", self.step["run"])],  # noqa: S607 - run the real workflow shell.
                check=False,
                env=env,
                cwd=temp,
                capture_output=True,
                text=True,
                timeout=30,
            )
            assert result.returncode == 0, result.stdout + result.stderr
            calls = [
                cast("list[str]", json.loads(line))
                for line in (temp / "trace").read_text().splitlines()
            ]
            cancelled = [call for call in calls if call[:2] == ["run", "cancel"]]
            return cancelled, calls, result.stdout

    def test_original_between_job_gap_does_not_cancel_licensed_run(self) -> None:
        completed = {
            **queued_job(2),
            "status": "completed",
            "conclusion": "success",
            "runner_id": 42,
        }
        fixture = {
            "run": run_metadata(".github/workflows/unity-tests.yml"),
            "jobs": [completed, queued_job()],
        }
        cancelled, _, output = self.execute_workflow(fixture)
        assert cancelled == [], output

    def test_assignment_after_initial_snapshot_is_protected(self) -> None:
        job = queued_job()
        fixture = {
            "run": run_metadata(),
            "jobs": [job],
            "fresh_jobs": [{**job, "runner_id": 42}],
        }
        cancelled, _, output = self.execute_workflow(fixture)
        assert cancelled == [], output

    def test_changed_labels_or_job_inventory_refuse_cancellation(self) -> None:
        job = queued_job()
        for fresh in (
            [{**job, "labels": ["self-hosted", "unavailable"]}],
            [job, queued_job(2)],
            [],
        ):
            with self.subTest(fresh=fresh):
                cancelled, _, output = self.execute_workflow(
                    {"run": run_metadata(), "jobs": [job], "fresh_jobs": fresh}
                )
                assert cancelled == [], output

    def test_unconfirmed_cancel_does_not_trigger_recovery(self) -> None:
        cancelled, calls, output = self.execute_workflow(
            {"run": run_metadata(), "jobs": [queued_job()], "cancel_failure": True}
        )
        assert len(cancelled) == 1
        assert "no cancellation confirmed" in output
        assert not any("/actions/workflows/" in call[-1] for call in calls)

    def test_licensed_work_cannot_race_even_after_final_read(self) -> None:
        for workflow in ("unity-tests.yml", "unity-benchmarks.yml"):
            with self.subTest(workflow=workflow):
                fixture = {
                    "run": run_metadata(f".github/workflows/{workflow}"),
                    "jobs": [queued_job()],
                }
                cancelled, _, output = self.execute_workflow(fixture)
                assert cancelled == [], output

    def test_genuine_never_started_run_is_cancelled_once(self) -> None:
        cancelled, calls, _ = self.execute_workflow({"run": run_metadata(), "jobs": [queued_job()]})
        assert cancelled == [["run", "cancel", "123", "--repo", "owner/repo"]]
        assert sum(call[-1].endswith("/actions/runs/123") for call in calls) == 2

    def test_rest_api_null_runner_id_preserves_never_started_recovery(self) -> None:
        job = {**queued_job(), "runner_id": None}
        cancelled, _, output = self.execute_workflow({"run": run_metadata(), "jobs": [job]})
        assert cancelled == [["run", "cancel", "123", "--repo", "owner/repo"]], output

    def test_final_attempt_and_state_changes_refuse_cancellation(self) -> None:
        for change in (
            {"run_attempt": 2},
            {"status": "in_progress"},
            {"head_sha": "b" * 40},
        ):
            with self.subTest(change=change):
                run = run_metadata()
                cancelled, _, output = self.execute_workflow(
                    {"run": run, "jobs": [queued_job()], "final_run": {**run, **change}}
                )
                assert cancelled == [], output

    def test_api_and_metadata_failures_refuse_cancellation(self) -> None:
        for option in ("api_failure", "invalid_json"):
            with self.subTest(option=option):
                cancelled, _, output = self.execute_workflow(
                    {"run": run_metadata(), "jobs": [queued_job()], option: True}
                )
                assert cancelled == [], output

    def test_existing_exclusions_and_rate_limit_still_apply(self) -> None:
        fixture = {"run": run_metadata(), "jobs": [queued_job()]}
        for capped, excluded in ((True, ""), (False, "build-publish-devcontainer.yml")):
            with self.subTest(capped=capped, excluded=excluded):
                cancelled, _, output = self.execute_workflow(
                    fixture, capped=capped, excluded=excluded
                )
                assert cancelled == [], output

    def test_all_license_lock_workflows_have_operator_protection(self) -> None:
        licensed = {
            path.name
            for path in (ROOT / ".github/workflows").glob("*.yml")
            if "ambiguous-organization-build-lock/.github/actions/acquire-build-lock@"
            in path.read_text()
        }
        assert {"unity-tests.yml", "unity-benchmarks.yml"}.issubset(licensed)
        assert licensed.issubset(GUARD.OPERATOR_WORKFLOWS), licensed - GUARD.OPERATOR_WORKFLOWS


if __name__ == "__main__":
    unittest.main(verbosity=2)
