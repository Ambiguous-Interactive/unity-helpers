"""Revalidate a never-started workflow before the watchdog requests cancellation."""

import argparse
import datetime
import json
import subprocess
from pathlib import Path

OPERATOR_WORKFLOWS = frozenset(
    {"release.yml", "unity-tests.yml", "unity-benchmarks.yml"}
)
IDENTITY_FIELDS = ("id", "run_attempt", "head_sha", "path", "event", "workflow_id")


def read_api(endpoint: str, *, paginate: bool = False):
    command = ["gh", "api"]
    if paginate:
        command.extend(["--paginate", "--slurp"])
    command.append(endpoint)
    result = subprocess.run(
        command, check=True, capture_output=True, text=True, timeout=30
    )
    return json.loads(result.stdout)


def eligible_run(run, expected, run_id: int, minimum_age: int, now) -> bool:
    if not isinstance(run, dict) or not isinstance(expected, dict):
        return False
    if any(
        field not in expected or run.get(field) != expected[field]
        for field in IDENTITY_FIELDS
    ):
        return False
    if (
        type(run.get("id")) is not int
        or run["id"] != run_id
        or type(run.get("run_attempt")) is not int
        or run["run_attempt"] < 1
        or type(run.get("workflow_id")) is not int
        or run["workflow_id"] < 1
        or not isinstance(run.get("head_sha"), str)
        or not run["head_sha"]
        or not isinstance(run.get("event"), str)
        or not run["event"]
        or run.get("status") != "queued"
        or "conclusion" not in run
        or run["conclusion"] is not None
        or not isinstance(run.get("path"), str)
        or not run["path"].startswith(".github/workflows/")
        or run["path"].split("@", 1)[0].rsplit("/", 1)[-1] in OPERATOR_WORKFLOWS
    ):
        return False
    started = run.get("run_started_at") or run.get("created_at")
    if not isinstance(started, str):
        return False
    try:
        timestamp = datetime.datetime.fromisoformat(started)
        return (
            timestamp.tzinfo is not None
            and (now - timestamp).total_seconds() >= minimum_age
        )
    except (ValueError, TypeError):
        return False


def never_started_jobs(pages, run_id: int) -> bool:
    if not isinstance(pages, list) or not pages:
        return False
    jobs = []
    total = None
    for page in pages:
        if not isinstance(page, dict) or not isinstance(page.get("jobs"), list):
            return False
        count = page.get("total_count")
        if (
            type(count) is not int
            or count < 1
            or (total is not None and count != total)
        ):
            return False
        total = count
        jobs.extend(page["jobs"])
    if len(jobs) != total:
        return False
    seen = set()
    for job in jobs:
        if not isinstance(job, dict):
            return False
        identifier = job.get("id")
        if type(identifier) is not int or identifier < 1 or identifier in seen:
            return False
        seen.add(identifier)
        if (
            job.get("run_id") != run_id
            or job.get("status") != "queued"
            or any(
                field not in job or job[field] is not None
                for field in ("started_at", "completed_at", "conclusion")
            )
            or type(job.get("runner_id")) is not int
            or job["runner_id"] != 0
            or job.get("runner_name") not in (None, "")
            or job.get("steps") != []
            or not isinstance(job.get("labels"), list)
            or not job["labels"]
            or any(not isinstance(label, str) or not label for label in job["labels"])
        ):
            return False
    return True


def cancel_if_eligible(
    repository: str, run_id: int, expected, expected_jobs, minimum_age: int
) -> bool:
    endpoint = f"repos/{repository}/actions/runs/{run_id}"
    now = datetime.datetime.now(datetime.UTC)
    run = read_api(endpoint)
    if not eligible_run(run, expected, run_id, minimum_age, now):
        return False
    if not isinstance(expected_jobs, list) or not never_started_jobs(
        [{"total_count": len(expected_jobs), "jobs": expected_jobs}], run_id
    ):
        return False
    pages = read_api(f"{endpoint}/jobs?filter=latest&per_page=100", paginate=True)
    if not never_started_jobs(pages, run_id):
        return False
    current_jobs = [job for page in pages for job in page["jobs"]]
    if sorted(current_jobs, key=lambda job: job["id"]) != sorted(
        expected_jobs, key=lambda job: job["id"]
    ):
        return False
    if not eligible_run(read_api(endpoint), expected, run_id, minimum_age, now):
        return False
    subprocess.run(
        ["gh", "run", "cancel", str(run_id), "--repo", repository],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--snapshot", type=Path, required=True)
    parser.add_argument("--jobs-snapshot", type=Path, required=True)
    parser.add_argument("--minimum-age-seconds", type=int, required=True)
    args = parser.parse_args()
    try:
        expected = json.loads(args.snapshot.read_text(encoding="utf-8"))
        expected_jobs = json.loads(args.jobs_snapshot.read_text(encoding="utf-8"))
        if args.run_id < 1 or args.minimum_age_seconds < 1:
            raise ValueError("Invalid cancellation limits")
        if cancel_if_eligible(
            args.repository,
            args.run_id,
            expected,
            expected_jobs,
            args.minimum_age_seconds,
        ):
            print(
                f"run {args.run_id}: cancellation accepted after fresh eligibility checks."
            )
            return 0
        print(
            f"run {args.run_id}: cancellation refused; licensed, progressed, changed or incomplete state needs operator review."
        )
    except (OSError, ValueError, subprocess.SubprocessError):
        print(
            f"run {args.run_id}: cancellation not confirmed; API, input or cancellation failure needs operator review."
        )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
