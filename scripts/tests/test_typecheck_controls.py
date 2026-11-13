"""Replay original control verdicts and prove scheduling, compiler and SDK guard boundaries."""

from __future__ import annotations

import json
import os
import re
import sys
import threading
from pathlib import Path
from typing import NotRequired, TypedDict, cast

import pytest
import yaml

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import typecheck_controls as controls
from check_runner import resolve_bash


class FrozenAttempt(TypedDict):
    exitCode: int
    output: str


class Classification(TypedDict):
    project: str
    control: str
    attempt: FrozenAttempt
    output: str | None


class FrozenArguments(TypedDict):
    requested: list[str]
    verbose: bool
    jobs: int


class ArgumentCase(TypedDict):
    args: list[str]
    output: NotRequired[FrozenArguments]
    error: NotRequired[str]


class DiagnosticCase(TypedDict):
    input: str
    output: list[str]


class StartupCase(TypedDict):
    attempt: FrozenAttempt
    output: bool


class Fixture(TypedDict):
    projects: list[dict[str, str]]
    controls: list[dict[str, object]]
    templates: dict[str, str]
    classifications: list[Classification]
    argumentsCases: list[ArgumentCase]
    diagnosticsCases: list[DiagnosticCase]
    startupCases: list[StartupCase]


ROOT = Path(__file__).resolve().parents[2]
FIXTURE = cast(
    "Fixture",
    json.loads(
        (Path(__file__).parent / "fixtures/typecheck-controls-original.json").read_text(
            encoding="utf-8"
        )
    ),
)
PROJECTS = {project.id: project for project in controls.load_projects(ROOT)}
CONTROL_BY_ID = {control.id: control for control in controls.CONTROLS}


def as_mapping(value: object) -> dict[str, object]:
    assert isinstance(value, dict)
    return cast("dict[str, object]", value)


def as_list(value: object) -> list[object]:
    assert isinstance(value, list)
    return cast("list[object]", value)


def workflow(file: str) -> dict[str, object]:
    return as_mapping(
        yaml.safe_load((ROOT / ".github/workflows" / file).read_text(encoding="utf-8"))
    )


def steps(job: object) -> list[dict[str, object]]:
    return [as_mapping(step) for step in as_list(as_mapping(job).get("steps", []))]


def test_original_metadata_and_rendered_control_bodies_are_preserved() -> None:
    assert len(PROJECTS) == len(FIXTURE["projects"]) == 5
    assert len(CONTROL_BY_ID) == len(FIXTURE["controls"]) == 3
    for project_metadata in FIXTURE["projects"]:
        project = PROJECTS[project_metadata["id"]]
        assert (project.tree, project.project, project.anchor, project.anchor_file) == (
            project_metadata["tree"],
            project_metadata["project"],
            project_metadata["anchor"],
            project_metadata["anchorFile"],
        )
    for control_metadata in FIXTURE["controls"]:
        control = CONTROL_BY_ID[cast("str", control_metadata["id"])]
        assert control.file_name == control_metadata["fileName"]
        assert control.meaning == control_metadata["meaning"]
        assert list(control.expected) == control_metadata["expected"]
        assert list(control.production_expected) == control_metadata.get("productionExpected", [])
        assert list(control.project_ids) == control_metadata.get("projectIds", [])
        assert control.property_name == control_metadata.get("property", "WallstopCheckControl")
        assert controls.render(control, "Example.Anchor") == FIXTURE["templates"][control.id]


@pytest.mark.parametrize("case", FIXTURE["classifications"])
def test_original_classification_observations(case: Classification) -> None:
    attempt = controls.Attempt(case["attempt"]["exitCode"], case["attempt"]["output"])
    assert (
        controls.classify(PROJECTS[case["project"]], CONTROL_BY_ID[case["control"]], attempt)
        == case["output"]
    )


@pytest.mark.parametrize("case", FIXTURE["argumentsCases"])
def test_original_argument_observations(case: ArgumentCase) -> None:
    if "error" in case:
        with pytest.raises(ValueError, match=re.escape(case["error"])):
            controls.parse_arguments(case["args"])
        return
    actual = controls.parse_arguments(case["args"])
    expected = case.get("output")
    assert expected is not None
    assert list(actual.requested) == expected["requested"]
    assert actual.verbose == expected["verbose"]
    expected_jobs = (
        expected["jobs"]
        if any(arg.startswith("--jobs=") for arg in case["args"])
        else controls.default_jobs()
    )
    assert actual.jobs == expected_jobs


@pytest.mark.parametrize("case", FIXTURE["diagnosticsCases"])
def test_original_ascii_diagnostic_boundaries(case: DiagnosticCase) -> None:
    assert sorted(controls.diagnostics_in(case["input"])) == case["output"]


@pytest.mark.parametrize("case", FIXTURE["startupCases"])
def test_original_migration_retry_classification(case: StartupCase) -> None:
    attempt = controls.Attempt(case["attempt"]["exitCode"], case["attempt"]["output"])
    assert controls.is_nuget_migration_startup_race(attempt) is case["output"]


@pytest.mark.parametrize("project", list(PROJECTS.values()), ids=lambda project: project.id)
def test_controls_carry_live_anchors_and_each_project_compiles_the_injected_file(
    project: controls.Project,
) -> None:
    body = (ROOT / project.project).read_text(encoding="utf-8")
    assert re.search(
        r'Condition="\x27\$\(WallstopCheckControl\)\x27 != \x27\x27"[\s\S]*?<Compile Include="\$\(WallstopCheckControl\)" />',
        body,
    )
    anchor_source = (ROOT / project.anchor_file).read_text(encoding="utf-8")
    assert re.search(
        r"(?:class|struct|interface)\s+" + re.escape(project.anchor.rsplit(".", 1)[-1]) + r"\b",
        anchor_source,
    )
    for control in controls.CONTROLS:
        source = controls.render(control, project.anchor)
        assert f"typeof({project.anchor})" in source
    compiler = controls.render(CONTROL_BY_ID["compiler"], project.anchor)
    assert compiler.index(f"typeof({project.anchor})") < compiler.index(
        "ControlTypeThisGateMustNotResolve"
    )


@pytest.mark.parametrize(
    ("failure", "expected_calls", "expected_failures"),
    [
        (controls.Attempt(1, "'NuGet-Migrations' errno == EEXIST"), 4, 2),
        (controls.Attempt(1, "ordinary failure"), 2, 2),
        (controls.Attempt(1, "'NuGet-Migrations' errno == EEXIST CS0246"), 2, 1),
    ],
)
def test_retry_is_bounded_and_never_discards_later_controls(
    failure: controls.Attempt, expected_calls: int, expected_failures: int, tmp_path: Path
) -> None:
    calls: list[tuple[str, Path, str]] = []

    def build(project: str, path: Path, property_name: str) -> controls.Attempt:
        calls.append((project, path, property_name))
        assert path.is_file()
        return failure

    result = controls.run_project(PROJECTS["runtime"], tmp_path, False, build)
    assert len(calls) == expected_calls
    assert len(result.failures) == expected_failures
    assert all(call[2] == "WallstopCheckControl" for call in calls)


def test_one_proven_startup_retry_can_then_pass_both_controls(tmp_path: Path) -> None:
    calls = 0

    def build(_project: str, path: Path, _property_name: str) -> controls.Attempt:
        nonlocal calls
        calls += 1
        if calls == 1:
            return controls.Attempt(1, "'NuGet-Migrations' errno == EEXIST")
        ids = (
            ("WPROTO001", "WUH003", "WUH013", "WUH020", "WUH026", "WUH019", "WUH021", "WUH023")
            if "Analyzers" in path.name
            else ("CS0246",)
        )
        return controls.Attempt(1, "\n".join(ids))

    result = controls.run_project(PROJECTS["runtime"], tmp_path, True, build)
    assert calls == 3
    assert result.failures == ()
    assert len(result.messages) == 4


def test_two_groups_overlap_while_runtime_and_integrations_remain_serial() -> None:
    gate = threading.Lock()
    editor_started = threading.Event()
    runtime_finished = threading.Event()
    active = 0
    maximum = 0

    def run(project: controls.Project) -> controls.ProjectResult:
        nonlocal active, maximum
        with gate:
            active += 1
            maximum = max(maximum, active)
        try:
            if project.id == "runtime":
                assert editor_started.wait(10)
                runtime_finished.set()
            elif project.id == "editor":
                editor_started.set()
                assert runtime_finished.wait(10)
            elif project.id == "integrations":
                assert runtime_finished.is_set()
            return controls.ProjectResult((), (project.id,))
        finally:
            with gate:
                active -= 1

    groups = [
        [PROJECTS["runtime"], PROJECTS["integrations"]],
        [PROJECTS["editor"]],
        [PROJECTS["tests"]],
    ]
    result = controls.run_project_groups(groups, 2, run)
    assert maximum == 2
    assert [[item.messages[0] for item in group] for group in result] == [
        ["runtime", "integrations"],
        ["editor"],
        ["tests"],
    ]
    assert controls.run_project_groups([], 1_000_000, run) == []
    with pytest.raises(ValueError, match="positive"):
        controls.run_project_groups(groups, 0, run)


def test_real_processes_preserve_exit_codes_utf8_and_binary_line_endings(tmp_path: Path) -> None:
    program = (
        "import os;os.write(1,b'first\\r\\n');os.write(2,b'second\\n\\xff');raise SystemExit(7)"
    )
    attempt = controls.run_command([sys.executable, "-c", program], tmp_path)
    assert attempt.exit_code == 7
    assert attempt.output == "first\r\nsecond\n\ufffd"
    missing = controls.run_command([str(tmp_path / "missing-compiler")], tmp_path)
    assert missing.exit_code == 1
    assert missing.output


@pytest.mark.parametrize("arguments", [["--only=missing"], ["--bad-option"]])
def test_real_cli_argument_failures_do_not_start_a_build(arguments: list[str]) -> None:
    import subprocess

    result = subprocess.run(
        [sys.executable, str(Path(controls.__file__)), *arguments],
        capture_output=True,
        encoding="utf-8",
        timeout=30,
        check=False,
    )
    assert result.returncode == 2
    assert result.stderr.startswith("[typecheck-controls]")
    assert "MSBuild" not in result.stdout


def test_missing_anchors_unknown_groups_and_empty_registries_cannot_pass(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    scripts = tmp_path / "scripts"
    scripts.mkdir()
    registry = scripts / "typecheck-projects.json"
    monkeypatch.setattr(controls, "ROOT", tmp_path)
    registry.write_text("[]", encoding="utf-8")
    assert controls.main([]) == 2
    assert "must contain projects" in capsys.readouterr().err
    project = dict(FIXTURE["projects"][0])
    registry.write_text(json.dumps([project]), encoding="utf-8")
    assert controls.main([]) == 2
    assert "anchor file" in capsys.readouterr().err
    (tmp_path / project["anchorFile"]).parent.mkdir(parents=True)
    (tmp_path / project["anchorFile"]).write_text("anchor", encoding="utf-8")
    project["id"] = "unassigned-project"
    registry.write_text(json.dumps([project]), encoding="utf-8")
    assert controls.main([]) == 2
    assert "No execution group" in capsys.readouterr().err


def test_host_phases_preserve_full_commands_isolation_and_serial_ci_controls() -> None:
    jobs = as_mapping(workflow("local-gates.yml")["jobs"])
    phase = as_mapping(jobs["typecheck-phases"])
    strategy = as_mapping(phase["strategy"])
    assert strategy["fail-fast"] is False
    assert as_mapping(strategy["matrix"])["phase"] == ["sources", "controls"]
    assert "max-parallel" not in strategy
    assert "continue-on-error" not in phase
    phase_steps = steps(phase)
    install = next(
        index
        for index, step in enumerate(phase_steps)
        if str(step.get("uses", "")).startswith("actions/setup-dotnet@")
    )
    guard = phase_steps[install + 1]
    commands = [
        step["run"]
        for step in phase_steps
        if step is not guard and isinstance(step.get("run"), str)
    ]
    assert commands == [
        "npm run typecheck:unity",
        "npm run test:enumeration",
        "npm run test:stable-assets",
        "npm run test:intmap",
        "npm run typecheck:controls -- --jobs=1",
    ]
    text = (ROOT / ".github/workflows/local-gates.yml").read_text(encoding="utf-8")
    phase_body = re.search(r"^  typecheck-phases:\n(?:(?: {4}[^\n]*|)\n)*", text, re.MULTILINE)
    assert phase_body is not None
    body = phase_body.group()
    assert "-typecheck-${{ matrix.phase }}-" in body
    assert "path: ~/.nuget/packages" in body
    assert "uses: actions/checkout@" in body
    assert not re.search(r"(?:upload|download)-artifact|restore-keys:|\b(?:bin|obj)/", body)
    for command in ("test:enumeration", "test:intmap"):
        step = next(step for step in phase_steps if step.get("run") == f"npm run {command}")
        assert step["if"] == "matrix.phase == 'sources'"
    for command, name in (("unity", "sources"), ("controls -- --jobs=1", "controls")):
        step = next(
            step for step in phase_steps if step.get("run") == f"npm run typecheck:{command}"
        )
        assert step["if"] == f"matrix.phase == '{name}'"
    assert controls.parse_arguments(["--jobs=1"]).jobs == 1
    assert 1 <= controls.default_jobs() <= 2
    assert controls.parse_arguments(["--jobs=1000000"]).jobs == 1_000_000


@pytest.mark.parametrize("state", ["success", "failure", "cancelled", "skipped", ""])
def test_aggregate_gate_runs_and_rejects_every_non_success_state(state: str) -> None:
    import shutil
    import subprocess

    jobs = as_mapping(workflow("local-gates.yml")["jobs"])
    job = as_mapping(jobs["typecheck"])
    assert job["name"] == "Type-check Runtime and tests"
    assert job["needs"] == "typecheck-phases"
    assert job["if"] == "always()"
    assert "continue-on-error" not in job
    step = next(step for step in steps(job) if isinstance(step.get("run"), str))
    assert (
        as_mapping(step["env"])["TYPECHECK_PHASE_RESULT"] == "${{ needs.typecheck-phases.result }}"
    )
    command = cast("str", step["run"])
    match = re.fullmatch(r"set -euo pipefail\nnode -e '([^'\n]+)'\n?", command)
    assert match is not None
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [node, "-e", match.group(1)],
        env={**os.environ, "TYPECHECK_PHASE_RESULT": state},
        capture_output=True,
        timeout=30,
        check=False,
    )
    assert (result.returncode == 0) is (state == "success")


def sdk_guards() -> list[tuple[str, str, dict[str, object], dict[str, object]]]:
    guards: list[tuple[str, str, dict[str, object], dict[str, object]]] = []
    for file in ("wproto-generator.yml", "repo-lint.yml", "local-gates.yml", "random-quality.yml"):
        for job_name, job in as_mapping(workflow(file)["jobs"]).items():
            job_steps = steps(job)
            for index, step in enumerate(job_steps):
                if str(step.get("uses", "")).startswith("actions/setup-dotnet@"):
                    guards.append((file, job_name, step, job_steps[index + 1]))
    return guards


@pytest.mark.parametrize("guard", sdk_guards(), ids=lambda guard: f"{guard[0]}/{guard[1]}")
def test_every_installed_sdk_is_selected_and_mismatches_are_rejected(
    guard: tuple[str, str, dict[str, object], dict[str, object]], tmp_path: Path
) -> None:
    import subprocess

    _, _, setup, selection = guard
    expected = as_mapping(setup["with"])["dotnet-version"]
    assert isinstance(expected, str)
    assert re.fullmatch(r"\d+\.\d+\.\d+", expected)
    installation = as_mapping(setup["env"])["DOTNET_INSTALL_DIR"]
    assert isinstance(installation, str)
    assert re.fullmatch(r"\$\{\{ runner\.temp \}\}/[a-z0-9-]+", installation)
    assert setup["id"]
    assert (
        as_mapping(selection["env"])["EXPECTED_DOTNET_SDK"]
        == "${{ steps." + str(setup["id"]) + ".outputs.dotnet-version }}"
    )
    assert selection["shell"] == "bash"
    stub = tmp_path / "dotnet"
    stub.write_text(
        '#!/bin/sh\n[ "$1" = --version ] || exit 91\nprintf "%s\\n" "$STUB_SDK_VERSION"\nexit "$STUB_SDK_STATUS"\n',
        encoding="utf-8",
        newline="\n",
    )
    stub.chmod(0o755)
    newer = f"{int(expected.split('.')[0]) + 1}.0.999"
    for selected, required, status, accepted in (
        (expected, expected, 0, True),
        (newer, expected, 0, False),
        ("", "", 0, False),
        (expected, expected, 17, False),
        (expected, "", 0, False),
    ):
        result = subprocess.run(
            [resolve_bash(), "-c", cast("str", selection["run"])],
            cwd=ROOT,
            env={
                **os.environ,
                "PATH": str(tmp_path) + os.pathsep + os.environ.get("PATH", ""),
                "EXPECTED_DOTNET_SDK": required,
                "STUB_SDK_VERSION": selected,
                "STUB_SDK_STATUS": str(status),
            },
            capture_output=True,
            timeout=30,
            check=False,
        )
        assert (result.returncode == 0) is accepted, result.stdout + result.stderr


def test_all_eight_sdk_installations_have_controls() -> None:
    assert len(sdk_guards()) == 8
