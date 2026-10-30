"""Run repository check registries concurrently and require configured completion evidence."""

from __future__ import annotations

import concurrent.futures
import json
import math
import os
import re
import subprocess
import sys
import threading
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Literal, cast

ROOT = Path(__file__).resolve().parents[1]
OUTPUT_LOCK = threading.Lock()


@dataclass(frozen=True)
class Check:
    id: str
    name: str
    run: str
    exclusive: bool = False
    completion_pattern: str | None = None


@dataclass(frozen=True)
class Result:
    id: str
    name: str
    ok: bool
    seconds: float


def emit(kind: Literal["output", "result"], value: object) -> None:
    with OUTPUT_LOCK:
        print(json.dumps({"kind": kind, "value": value}, ensure_ascii=True), flush=True)


def write(text: str) -> None:
    emit("output", text)


def default_jobs(environment: dict[str, str] | None = None) -> int:
    resolved_environment = os.environ if environment is None else environment
    cores = (
        len(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else os.cpu_count() or 1
    )
    try:
        quota, period = Path("/sys/fs/cgroup/cpu.max").read_text(encoding="ascii").split()
        if quota != "max" and int(quota) > 0 and int(period) > 0:
            cores = min(cores, math.ceil(int(quota) / int(period)))
    except (OSError, UnicodeError, ValueError):
        pass
    return max(1, cores if resolved_environment.get("GITHUB_ACTIONS") == "true" else min(4, cores))


def parse_arguments(argv: list[str]) -> dict[str, object]:
    only: list[str] = []
    listing = False
    jobs = 0
    invalid: str | None = None
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--list":
            listing = True
        elif argument in {"--only", "--jobs"}:
            index += 1
            value = argv[index] if index < len(argv) else None
            if argument == "--only":
                if value:
                    only.extend(item.strip() for item in value.split(",") if item.strip())
            else:
                try:
                    numeric = "" if value is None else value.strip()
                    if re.fullmatch(r"0[xX][0-9a-fA-F]+|0[bB][01]+|0[oO][0-7]+", numeric):
                        number = float(int(numeric, 0))
                    elif re.fullmatch(
                        r"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?", numeric
                    ):
                        number = float(numeric)
                    else:
                        raise ValueError("Expected a numeric worker count")
                    if number < 1 or not number.is_integer():
                        raise ValueError("Expected a positive integer")
                    jobs = int(number)
                except (ValueError, OverflowError):
                    invalid = "undefined" if value is None else value
        index += 1
    return {
        "only": list(dict.fromkeys(only)),
        "list": listing,
        "jobs": jobs or default_jobs(),
        "jobsInvalid": invalid,
    }


def run_check(check: Check) -> Result:
    started = time.monotonic()
    try:
        process = subprocess.run(  # noqa: S603 - reviewed repository shell commands are intentional.
            ["bash", "-c", check.run],  # noqa: S607 - preserve the command's PATH lookup contract.
            cwd=ROOT,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
        status = process.returncode
        output = process.stdout.decode("utf-8", errors="replace")
        reason = f"signal {-status}" if status < 0 else f"exit {status}"
    except OSError as error:
        status = None
        output = f"{error.__class__.__name__}: {error}\n"
        if isinstance(error, FileNotFoundError):
            output = f"ENOENT: {output}"
        reason = "signal spawn-error"
    seconds = time.monotonic() - started
    complete = (
        check.completion_pattern is None
        or re.search(check.completion_pattern, output, re.MULTILINE) is not None
    )
    ok = status == 0 and complete
    block = f"::group::{check.name} ({check.id})\n$ {check.run}\n{output}::endgroup::\n"
    if not ok:
        if status == 0 and not complete:
            reason = "exit 0 without required completion evidence"
        block += f"::error::{check.name} ({check.id}) failed with {reason}.\n"
    write(block)
    return Result(check.id, check.name, ok, seconds)


def run_checks(checks: list[Check], jobs: int) -> list[Result]:
    if jobs < 1:
        raise ValueError("Worker count must be positive")
    results: dict[int, Result] = {}
    shared: list[tuple[int, Check]] = []
    for index, check in enumerate(checks):
        if check.exclusive:
            results[index] = run_check(check)
        else:
            shared.append((index, check))
    if shared:
        with concurrent.futures.ThreadPoolExecutor(max_workers=min(jobs, len(shared))) as executor:
            pending = {executor.submit(run_check, check): index for index, check in shared}
            for future in concurrent.futures.as_completed(pending):
                results[pending[future]] = future.result()
    return [results[index] for index in range(len(checks))]


def step_summary(results: list[Result], wall: float, jobs: int, title: str, command: str) -> None:
    destination = os.environ.get("GITHUB_STEP_SUMMARY")
    if not destination:
        return
    failures = [result for result in results if not result.ok]
    total = sum(result.seconds for result in results)
    lines = [
        f"## {title}",
        "",
        f"{len(results) - len(failures)}/{len(results)} checks passed in {wall:.1f}s wall clock across {jobs} job(s) ({total:.1f}s serial).",
        "",
    ]
    if failures:
        lines.extend(["| Failed check | id |", "| --- | --- |"])
        lines.extend(f"| {result.name} | `{result.id}` |" for result in failures)
        lines.extend(
            [
                "",
                "Reproduce a single check locally with:",
                "",
                "```bash",
                f"{command} --only {','.join(result.id for result in failures)}",
                "```",
                "",
            ]
        )
    lines.extend(
        [
            "<details><summary>Ten slowest checks</summary>",
            "",
            "| Check | id | Seconds |",
            "| --- | --- | ---: |",
        ]
    )
    lines.extend(
        f"| {result.name} | `{result.id}` | {result.seconds:.1f} |"
        for result in sorted(results, key=lambda result: result.seconds, reverse=True)[:10]
    )
    lines.extend(["", "</details>", ""])
    with Path(destination).open("a", encoding="utf-8", newline="\n") as stream:
        stream.write("\n".join(lines) + "\n")


def run_registry(checks: list[Check], title: str, command: str, argv: list[str]) -> int:
    selection = parse_arguments(argv)
    invalid = selection["jobsInvalid"]
    if invalid is not None:
        write(f"::error::--jobs must be a positive integer, got: {invalid}\n")
        return 1
    if selection["list"]:
        for check in checks:
            write(f"{check.id}\n")
        return 0
    only = cast("list[str]", selection["only"])
    known = {check.id for check in checks}
    unknown = [item for item in only if item not in known]
    if unknown:
        write(f"::error::Unknown check id(s): {', '.join(unknown)}\n")
        return 1
    selected = [check for check in checks if not only or check.id in only]
    if not selected:
        write("::error::No checks selected.\n")
        return 1
    jobs = cast("int", selection["jobs"])
    start = time.monotonic()
    results = run_checks(selected, jobs)
    wall = time.monotonic() - start
    step_summary(results, wall, jobs, title, command)
    write(
        "\n"
        + "".join(
            f"{'PASS' if result.ok else 'FAIL'}  {result.seconds:6.1f}s  {result.id}\n"
            for result in results
        )
    )
    failures = [result.id for result in results if not result.ok]
    total = sum(result.seconds for result in results)
    write(
        f"\n{len(results) - len(failures)}/{len(results)} checks passed in {wall:.1f}s wall clock across {jobs} job(s) ({total:.1f}s serial).\n"
    )
    if failures:
        write(
            f"\nFailed: {', '.join(failures)}\nReproduce locally: {command} --only {','.join(failures)}\n"
        )
    return int(bool(failures))


def mapping(value: object) -> dict[str, object]:
    if not isinstance(value, dict) or not all(
        isinstance(key, str) for key in cast("dict[object, object]", value)
    ):
        raise ValueError("Expected a string-keyed object")
    return cast("dict[str, object]", value)


def string(value: object) -> str:
    if not isinstance(value, str):
        raise ValueError("Expected a string")
    return value


def strings(value: object) -> list[str]:
    if not isinstance(value, list):
        raise ValueError("Expected a string array")
    return [string(item) for item in cast("list[object]", value)]


def read_checks(value: object) -> list[Check]:
    if not isinstance(value, list):
        raise ValueError("Expected a check array")
    checks: list[Check] = []
    for item in cast("list[object]", value):
        fields = mapping(item)
        pattern = fields.get("completionPattern")
        if pattern is not None:
            re.compile(string(pattern))
        checks.append(
            Check(
                string(fields["id"]),
                string(fields["name"]),
                string(fields["run"]),
                fields.get("exclusive") is True,
                None if pattern is None else string(pattern),
            )
        )
    return checks


def dispatch(request: dict[str, object]) -> object:
    operation = request.get("operation")
    if operation == "defaultJobs":
        raw = request.get("environment")
        environment = (
            None if raw is None else {key: string(value) for key, value in mapping(raw).items()}
        )
        return default_jobs(environment)
    if operation == "parseArguments":
        return parse_arguments(strings(request["argv"]))
    checks = read_checks(request["checks"])
    if operation == "runRegistry":
        return run_registry(
            checks,
            string(request["title"]),
            string(request["command"]),
            strings(request["argv"]),
        )
    if operation == "runChecks":
        count = request.get("jobs", default_jobs())
        if not isinstance(count, int) or isinstance(count, bool):
            raise ValueError("Worker count must be an integer")
        return [asdict(result) for result in run_checks(checks, count)]
    raise ValueError("Unknown check runner operation")


def main() -> int:
    try:
        request = mapping(cast("object", json.load(sys.stdin)))
        emit("result", dispatch(request))
    except (OSError, ValueError, KeyError, re.error) as error:
        print(f"Check runner failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
