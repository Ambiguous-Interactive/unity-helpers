"""Prove compiler inputs and governed source trees with independently failing controls."""

from __future__ import annotations

import concurrent.futures
import contextlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import cast

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
TEMPLATES = Path(__file__).with_name("typecheck-control-sources")
DIAGNOSTIC_PATTERN = re.compile(r"\b(CS\d{4}|WPROTO\d{3}|WUH\d{3})\b", re.ASCII)
JS_WHITESPACE = "\t\v\f \u00a0\ufeff\n\r\u2028\u2029\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u202f\u205f\u3000"
DECIMAL = re.compile(r"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?")


@dataclass(frozen=True)
class Project:
    id: str
    tree: str
    project: str
    anchor: str
    anchor_file: str


@dataclass(frozen=True)
class Control:
    id: str
    file_name: str
    expected: tuple[str, ...]
    meaning: str
    production_expected: tuple[str, ...] = ()
    project_ids: tuple[str, ...] = ()
    property_name: str = "WallstopCheckControl"


@dataclass(frozen=True)
class Attempt:
    exit_code: int
    output: str


@dataclass(frozen=True)
class Arguments:
    requested: tuple[str, ...]
    verbose: bool
    jobs: int


@dataclass(frozen=True)
class ProjectResult:
    failures: tuple[str, ...]
    messages: tuple[str, ...]


BuildControl = Callable[[str, Path, str], Attempt]
CONTROLS = (
    Control(
        "analyzers",
        "WallstopCheckControlAnalyzers.cs",
        ("WPROTO001", "WUH003", "WUH013", "WUH020", "WUH026"),
        "the shipped generators and analyzers report, including the production collection-size and struct equality opt-ins",
        production_expected=("WUH019", "WUH021", "WUH023"),
    ),
    Control(
        "compiler",
        "WallstopCheckControlCompiler.cs",
        ("CS0246",),
        "the compiler itself is reporting, and the project is not silencing its errors",
    ),
    Control(
        "excluded-loops",
        "WallstopCheckControlExcludedLoops.cs",
        ("WUH013", "WUH019", "WUH021", "WUH023", "WUH026", "WUH028"),
        "the persistent audit rejects collection-size reads and missing struct equality in sources outside normal compilation",
        project_ids=("editor",),
        property_name="WallstopCountingLoopAuditControl",
    ),
)


def load_projects(root: Path) -> list[Project]:
    content = cast(
        "object", json.loads((root / "scripts/typecheck-projects.json").read_text(encoding="utf-8"))
    )
    if not isinstance(content, list) or not content:
        raise ValueError("The typecheck project registry must contain projects.")
    projects: list[Project] = []
    for raw in cast("list[object]", content):
        if not isinstance(raw, dict):
            raise ValueError("Every typecheck project must be an object.")
        item = cast("dict[str, object]", raw)
        values: list[str] = []
        for name in ("id", "tree", "project", "anchor", "anchorFile"):
            value = item.get(name)
            if not isinstance(value, str) or not value.strip():
                raise ValueError(f"Typecheck project requires {name}.")
            values.append(value)
        projects.append(Project(*values))
    if len({project.id for project in projects}) != len(projects):
        raise ValueError("The typecheck project registry contains duplicate ids.")
    return projects


def default_jobs() -> int:
    cores = os.cpu_count() or 1
    affinity = getattr(os, "sched_getaffinity", None)
    if callable(affinity):
        with contextlib.suppress(OSError):
            cores = len(cast("Callable[[int], set[int]]", affinity)(0))
    return min(2, max(1, cores))


def parse_jobs(argument: str) -> int:
    raw = argument.removeprefix("--jobs=").strip(JS_WHITESPACE)
    try:
        if raw.lower().startswith(("0x", "0b", "0o")):
            if "_" in raw:
                raise ValueError
            count = int(raw, 0)
        elif DECIMAL.fullmatch(raw):
            number = float(raw)
            if not math.isfinite(number) or not number.is_integer():
                raise ValueError
            count = int(number)
        else:
            raise ValueError
        if count < 1:
            raise ValueError
    except ValueError as error:
        raise ValueError(f"--jobs must be a positive integer, got {argument}.") from error
    return count


def parse_arguments(arguments: Sequence[str]) -> Arguments:
    requested: list[str] = []
    verbose = False
    jobs = default_jobs()
    for argument in arguments:
        if argument == "--verbose":
            verbose = True
        elif argument.startswith("--only="):
            requested.extend(argument.removeprefix("--only=").split(","))
        elif argument.startswith("--jobs="):
            jobs = parse_jobs(argument)
        else:
            raise ValueError(
                f"Unknown option {argument}. Use --only=<id>[,<id>], --jobs=<count>, or --verbose."
            )
    return Arguments(tuple(entry for entry in requested if entry), verbose, jobs)


def render(control: Control, anchor: str) -> str:
    source = (TEMPLATES / f"{control.id}.cs.template").read_text(encoding="utf-8")
    return source.replace("__CHECK_ANCHOR__", anchor)


def run_command(command: list[str], root: Path) -> Attempt:
    try:
        result = subprocess.run(  # noqa: S603 - fixed SDK executable and shell-free MSBuild arguments.
            command,
            cwd=root,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
    except OSError as error:
        return Attempt(1, f"{error}\n")
    return Attempt(
        result.returncode if result.returncode >= 0 else 1,
        result.stdout.decode("utf-8", errors="replace"),
    )


def build(project: str, control_path: Path, property_name: str) -> Attempt:
    executable = shutil.which("dotnet")
    if executable is None:
        return Attempt(1, "dotnet is not installed.\n")
    command = [
        executable,
        "build",
        str(ROOT / project),
        "--nologo",
        "-v",
        "minimal",
        "-p:UseSharedCompilation=false",
        f"-p:{property_name}={control_path}",
    ]
    return run_command(command, ROOT)


def diagnostics_in(output: str) -> set[str]:
    return set(DIAGNOSTIC_PATTERN.findall(output))


def is_nuget_migration_startup_race(attempt: Attempt) -> bool:
    return (
        attempt.exit_code != 0
        and "'NuGet-Migrations'" in attempt.output
        and "errno == EEXIST" in attempt.output
        and not diagnostics_in(attempt.output)
    )


def expected_diagnostics(project: Project, control: Control) -> tuple[str, ...]:
    production = (
        control.production_expected if project.id in {"runtime", "editor", "integrations"} else ()
    )
    return (*control.expected, *production)


def classify(project: Project, control: Control, attempt: Attempt) -> str | None:
    reported = diagnostics_in(attempt.output)
    expected = expected_diagnostics(project, control)
    missing = [identifier for identifier in expected if identifier not in reported]
    unexpected = sorted(reported - set(expected))
    expectation = f"Expected {' and '.join(expected)} so that {control.meaning}."
    if attempt.exit_code == 0:
        return (
            f"{project.id} {control.id}: the build SUCCEEDED with a control defect in it. "
            + expectation
        )
    if missing:
        return (
            f"{project.id} {control.id}: the build failed but never reported {', '.join(missing)}. "
            f"{expectation} Reported: {', '.join(sorted(reported)) or 'nothing'}."
        )
    if unexpected:
        return (
            f"{project.id} {control.id}: the control fired, but the build also reported "
            f"{', '.join(unexpected)}. Either {project.tree} does not type-check on its own -- run "
            f"the ordinary gate and fix that first -- or the anchor {project.anchor} no longer "
            "resolves, which means this project has stopped compiling that tree."
        )
    return None


def run_project(
    project: Project, control_root: Path, verbose: bool, build_control: BuildControl = build
) -> ProjectResult:
    failures: list[str] = []
    messages: list[str] = []
    for control in CONTROLS:
        if control.project_ids and project.id not in control.project_ids:
            continue
        control_path = control_root / f"{project.id}-{control.file_name}"
        control_path.write_text(render(control, project.anchor), encoding="utf-8", newline="\n")
        attempt = build_control(project.project, control_path, control.property_name)
        if is_nuget_migration_startup_race(attempt):
            attempt = build_control(project.project, control_path, control.property_name)
        failure = classify(project, control, attempt)
        if verbose or failure is not None:
            messages.append(attempt.output)
        if failure is not None:
            failures.append(failure)
        else:
            messages.append(
                f"  [PASS] {project.id}: the {control.id} control over {project.tree} reported "
                f"{', '.join(expected_diagnostics(project, control))} and nothing else\n"
            )
    return ProjectResult(tuple(failures), tuple(messages))


def run_project_groups(
    groups: Sequence[Sequence[Project]],
    jobs: int,
    run: Callable[[Project], ProjectResult],
) -> list[list[ProjectResult]]:
    if jobs < 1:
        raise ValueError("Worker count must be positive.")
    if not groups:
        return []

    def run_group(group: Sequence[Project]) -> list[ProjectResult]:
        return [run(project) for project in group]

    with concurrent.futures.ThreadPoolExecutor(max_workers=min(jobs, len(groups))) as executor:
        return list(executor.map(run_group, groups))


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    try:
        options = parse_arguments(sys.argv[1:] if arguments is None else arguments)
        available = load_projects(ROOT)
        projects = [
            project
            for project in available
            if not options.requested or project.id in options.requested
        ]
        if not projects:
            raise ValueError(
                f"No check project matched {', '.join(options.requested)}. Known ids: "
                + ", ".join(project.id for project in available)
            )
        for project in projects:
            if not (ROOT / project.anchor_file).exists():
                raise ValueError(
                    f"{project.id}: the anchor file {project.anchor_file} is gone, so its control would "
                    f"report a moved type as a broken gate. Point {project.id} at another long-lived "
                    f"public type in {project.tree}."
                )
        by_id = {project.id: project for project in projects}
        groups = [
            [by_id[identifier] for identifier in identifiers if identifier in by_id]
            for identifiers in (
                ("runtime", "integrations"),
                ("editor",),
                ("tests",),
                ("editor-tests",),
            )
        ]
        groups = [group for group in groups if group]
        covered = {project.id for group in groups for project in group}
        uncovered = sorted(set(by_id) - covered)
        if uncovered:
            raise ValueError(f"No execution group for check project(s): {', '.join(uncovered)}.")
        failures: list[str] = []
        with tempfile.TemporaryDirectory(prefix="unity-helpers-check-controls-") as temporary:
            control_root = Path(temporary)

            def run(project: Project) -> ProjectResult:
                return run_project(project, control_root, options.verbose)

            for group_results in run_project_groups(groups, options.jobs, run):
                for result in group_results:
                    failures.extend(result.failures)
                    sys.stdout.write("".join(result.messages))
        print()
        if failures:
            print(f"[typecheck-controls] {len(failures)} control(s) failed:", file=sys.stderr)
            for failure in failures:
                print(f"  - {failure}", file=sys.stderr)
            print(
                "[typecheck-controls] A control that does not fire means the gate beside it is not "
                "measuring what its green run claims.",
                file=sys.stderr,
            )
            return 1
        print(
            f"[typecheck-controls] OK: {len(projects)} check project(s), {len(CONTROLS)} control(s) "
            "available by scope, every expected diagnostic reported and nothing else across "
            f"{min(options.jobs, len(groups))} worker(s)."
        )
        return 0
    except Exception as error:
        print(f"[typecheck-controls] {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
