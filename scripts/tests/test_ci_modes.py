"""Exercise real worker processes, shared-session checks and complete mode outcomes."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest
import yaml

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
from run_ci_modes import ROOT, Mode, build_modes, git, isolate_sources, read_selection, run

PROFILES = {name: f"Subject.{name}" for name in ("editmode", "playmode", "standalone")}


@pytest.mark.parametrize("single", [False, True])
def test_mode_configuration_retains_profiles_builds_and_independent_projects(
    tmp_path: Path, single: bool
) -> None:
    modes = build_modes(
        "6000.6.0f1",
        list(PROFILES),
        PROFILES,
        repository=ROOT,
        workspace=tmp_path / "workspace",
        artifacts=tmp_path / "artifacts",
        session=tmp_path / "session",
        single_threaded=single,
    )
    assert [mode.name for mode in modes] == list(PROFILES) + ([] if single else ["proto_only"])
    assert len({str(mode.parameters["ArtifactsPath"]) for mode in modes}) == len(modes)
    for mode in modes[:3]:
        options = mode.parameters
        assert options["AssemblyNames"] == PROFILES[mode.name]
        assert options["ProjectRoot"] == str(tmp_path / "workspace")
        assert options["ReleaseCodeOptimization"] is True
        assert options["ReleasePlayerBuild"] is True
        assert options["Il2CppCompilerConfiguration"] == "Release"
        assert options.get("ShareEditorProject", False) == (mode.name == "editmode")
        assert options.get("IsolateCompilationFixtures", False) == (mode.name == "editmode")
        if single:
            assert options["ProjectScope"] == "single-threaded"
            assert options["AdditionalScriptingDefines"] == [
                "SINGLE_THREADED",
                "WALLSTOP_CONCAVE_HULL_STATS",
            ]
            assert "IncludeIntegrations" not in options
        else:
            assert options["IncludeIntegrations"] is True
    assert modes[2].parameters["ManagedStrippingLevel"] == "High"
    if not single:
        proto = modes[3].parameters
        assert proto["TestMode"] == "standalone"
        assert proto["AssemblyNames"] == "WallstopStudios.UnityHelpers.Tests.ProtoOnly"
        assert proto["ProjectScope"] == "proto-only"
        assert proto["AdditionalScriptingDefines"] == ["WALLSTOP_PROTO_ONLY"]
        assert proto["TestCategory"] == ""
        assert "IncludeIntegrations" not in proto


@pytest.mark.parametrize(
    ("selected", "profiles"),
    [
        ([], PROFILES),
        (["editmode", "editmode"], PROFILES),
        (["export"], PROFILES),
        ([False], PROFILES),
        ("editmode", PROFILES),
        (["editmode"], {}),
        (["editmode"], {**PROFILES, "playmode": " "}),
        (["editmode"], {**PROFILES, "extra": "Unexpected"}),
        (["editmode"], {**PROFILES, "playmode": False}),
    ],
)
def test_bad_selection_is_rejected(selected: object, profiles: object) -> None:
    with pytest.raises(ValueError, match="Unity"):
        read_selection(json.dumps(selected), json.dumps(profiles))


def test_selection_limits_and_order() -> None:
    assert read_selection('["standalone","editmode"]', json.dumps(PROFILES))[0] == [
        "standalone",
        "editmode",
    ]
    for raw, profiles in [(" " * 1025, "{}"), ("[]", " " * 65537)]:
        with pytest.raises(ValueError, match="size limit"):
            read_selection(raw, profiles)


def make_control(tmp_path: Path) -> tuple[list[Mode], Mode, dict[str, str], str]:
    shell = shutil.which("pwsh")
    assert shell is not None
    runner = tmp_path / "worker with spaces.ps1"
    helper = str(ROOT / "scripts/unity/lib/license-session.ps1").replace("'", "''")
    runner.write_text(
        """[CmdletBinding()] param([string]$LicenseSessionPath, [switch]$PrepareLicenseOnly, [string]$ArtifactsPath,
        [string]$UnityVersion, [string]$TestMode, [string]$AssemblyNames, [string]$RepoRoot,
        [string]$ProjectRoot, [switch]$ReleaseCodeOptimization, [switch]$ReleasePlayerBuild,
        [string]$Il2CppCompilerConfiguration, [switch]$IncludeIntegrations, [switch]$ShareEditorProject,
        [switch]$IsolateCompilationFixtures, [string]$ManagedStrippingLevel, [string]$ProjectScope,
        [string[]]$AdditionalScriptingDefines, [string]$TestCategory, [switch]$GenerateOnly)
        $ErrorActionPreference = 'Stop'
        . 'HELPER'
        if ($GenerateOnly) { return }
        $state = if ($PrepareLicenseOnly) { 'requested' } else { 'activated' }
        $session = Read-UnityLicenseSession -Path $LicenseSessionPath -UnityVersion $UnityVersion -EditorPath $env:UNITY_EDITOR_PATH -State $state
        if ($PrepareLicenseOnly) {
            if ($env:CONTROL_PREPARATION_FAILURE -eq '1') { throw 'Controlled preparation failure.' }
            Complete-UnityLicenseSession -Path $LicenseSessionPath -Session $session
            return
        }
        [IO.File]::WriteAllText((Join-Path $env:CONTROL_EVENTS ($TestMode + '.started')), [string]$PID)
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (@(Get-ChildItem -LiteralPath $env:CONTROL_EVENTS -Filter '*.started').Count -lt 3) {
            if ($deadline -lt [DateTime]::UtcNow) { throw 'Workers did not overlap.' }
            Start-Sleep -Milliseconds 10
        }
        if ($env:CONTROL_HANG -eq $TestMode) {
            $start = [Diagnostics.ProcessStartInfo]::new($env:CONTROL_PYTHON)
            $start.UseShellExecute = $false
            $start.ArgumentList.Add($env:CONTROL_SLEEPER)
            $child = [Diagnostics.Process]::Start($start)
            [IO.File]::WriteAllText((Join-Path $env:CONTROL_EVENTS ($TestMode + '.child')), [string]$child.Id)
            $child.WaitForExit()
        }
        if ($env:CONTROL_FAIL -eq $TestMode) { throw 'Controlled mode failure.' }
        [IO.File]::WriteAllText((Join-Path $ArtifactsPath 'fixture-options.json'), (@{ RepoRoot = $RepoRoot; AssemblyNames = $AssemblyNames } | ConvertTo-Json))
        [IO.File]::WriteAllText((Join-Path $env:CONTROL_EVENTS ($TestMode + '.completed')), 'done')
        """.replace("HELPER", helper),
        encoding="utf-8",
    )
    session_path = tmp_path / "session.json"
    identity = {
        name: os.environ.get(name) for name in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_JOB")
    }
    session_path.write_text(
        json.dumps(
            {
                "SchemaVersion": 1,
                "State": "requested",
                "OwnerProcessId": os.getpid(),
                "UnityVersion": "6000.6.0f1",
                "EditorPath": str(tmp_path / "editor with spaces.exe"),
                "Identity": identity,
            }
        ),
        encoding="utf-8",
    )
    events = tmp_path / "events"
    events.mkdir()
    sleeper = tmp_path / "sleeper with spaces.py"
    sleeper.write_text("import time\ntime.sleep(60)\n", encoding="utf-8")
    environment = dict(
        os.environ,
        UH_MODE_RUNNER=str(runner),
        UH_CENTRAL_LICENSE_RETURN="true",
        UH_UNITY_SESSION_OWNER_PID=str(os.getpid()),
        UNITY_EDITOR_PATH=str(tmp_path / "editor with spaces.exe"),
        CONTROL_EVENTS=str(events),
        CONTROL_PYTHON=sys.executable,
        CONTROL_SLEEPER=str(sleeper),
    )
    modes = [
        Mode(
            name,
            {
                "LicenseSessionPath": str(session_path),
                "ArtifactsPath": str(tmp_path / name),
                "UnityVersion": "6000.6.0f1",
                "TestMode": name,
            },
        )
        for name in PROFILES
    ]
    preparation = Mode("prepare", {**modes[0].parameters, "PrepareLicenseOnly": True})
    return modes, preparation, environment, shell


@pytest.mark.parametrize("failed", ["", "playmode"])
def test_real_workers_overlap_and_keep_every_outcome(tmp_path: Path, failed: str) -> None:
    modes, preparation, environment, shell = make_control(tmp_path)
    environment["CONTROL_FAIL"] = failed
    output = tmp_path / "github-output"
    output.write_text("previous=value\n", encoding="utf-8")
    success = run(
        modes,
        preparation,
        private=tmp_path,
        shell=shell,
        environment=environment,
        timeout=20,
        receipt=tmp_path / "receipt.json",
        output=output,
    )
    assert success == (failed == "")
    receipt = json.loads((tmp_path / "receipt.json").read_text(encoding="utf-8"))
    expected = {mode.name: "failure" if mode.name == failed else "success" for mode in modes}
    assert receipt["Outcomes"] == expected
    assert receipt["ControllerCompleted"] is True
    assert receipt["CleanupFailed"] is False
    assert len(receipt["Workers"]) == 3
    assert len(list((tmp_path / "events").glob("*.completed"))) == (2 if failed else 3)
    assert output.read_text(encoding="utf-8") == "previous=value\n" + "".join(
        f"{name}={outcome}\n" for name, outcome in expected.items()
    )


def test_failed_preparation_never_launches_a_mode(tmp_path: Path) -> None:
    modes, preparation, environment, shell = make_control(tmp_path)
    environment["CONTROL_PREPARATION_FAILURE"] = "1"
    with pytest.raises(RuntimeError, match="license preparation failed"):
        run(
            modes,
            preparation,
            private=tmp_path,
            shell=shell,
            environment=environment,
            timeout=20,
            receipt=tmp_path / "receipt.json",
            output=None,
        )
    receipt = json.loads((tmp_path / "receipt.json").read_text(encoding="utf-8"))
    assert receipt["Workers"] == []
    assert receipt["ControllerCompleted"] is False
    assert set(receipt["Outcomes"].values()) == {"failure"}
    assert list((tmp_path / "events").iterdir()) == []


def test_timeout_kills_and_reaps_owned_workers(tmp_path: Path) -> None:
    modes, preparation, environment, shell = make_control(tmp_path)
    environment["CONTROL_HANG"] = "playmode"
    with pytest.raises(TimeoutError):
        run(
            modes,
            preparation,
            private=tmp_path,
            shell=shell,
            environment=environment,
            timeout=15,
            receipt=tmp_path / "receipt.json",
            output=None,
        )
    receipt = json.loads((tmp_path / "receipt.json").read_text(encoding="utf-8"))
    assert receipt["CleanupFailed"] is False
    assert receipt["ControllerCompleted"] is False
    assert len(receipt["Workers"]) == 3
    hung = next(item for item in receipt["Workers"] if item["Mode"] == "playmode")
    assert hung["Forced"] is True
    assert hung["ExitCode"] != 0
    assert set(receipt["Outcomes"].values()) == {"failure"}
    assert len(list((tmp_path / "events").glob("*.completed"))) == 2
    child_id = int((tmp_path / "events/playmode.child").read_text(encoding="utf-8"))
    if os.name == "nt":
        status = subprocess.run(
            [
                shell,
                "-NoProfile",
                "-Command",
                f"if ($null -ne (Get-Process -Id {child_id} -ErrorAction SilentlyContinue)) {{ exit 1 }}",
            ],
            capture_output=True,
            check=False,
            timeout=20,
        )
        assert status.returncode == 0
    else:
        status_path = Path(f"/proc/{child_id}/stat")
        assert not status_path.exists() or status_path.read_text().rsplit(")", 1)[
            1
        ].strip().startswith("Z")


@pytest.mark.parametrize(
    "damage", ["state", "owner", "version", "editor", "job", "central", "size"]
)
def test_invalid_session_never_activates_or_starts_tests(tmp_path: Path, damage: str) -> None:
    _modes, _preparation, environment, shell = make_control(tmp_path)
    path = tmp_path / "session.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    if damage == "state":
        document["State"] = "activated"
    elif damage == "owner":
        document["OwnerProcessId"] += 1
    elif damage == "version":
        document["UnityVersion"] = "2021.3.45f1"
    elif damage == "editor":
        document["EditorPath"] = str(tmp_path / "different.exe")
    elif damage == "job":
        document["Identity"]["GITHUB_JOB"] = "different"
    elif damage == "central":
        environment["UH_CENTRAL_LICENSE_RETURN"] = "false"
    path.write_text(" " * 16385 if damage == "size" else json.dumps(document), encoding="utf-8")
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            environment["UH_MODE_RUNNER"],
            "-PrepareLicenseOnly",
            "-LicenseSessionPath",
            str(path),
            "-UnityVersion",
            "6000.6.0f1",
        ],
        env=environment,
        capture_output=True,
        check=False,
        timeout=20,
    )
    assert result.returncode != 0
    assert list((tmp_path / "events").iterdir()) == []


def test_workflow_requires_controller_and_mode_success_before_using_results() -> None:
    document = cast(
        "dict[str, object]",
        yaml.safe_load((ROOT / ".github/workflows/unity-tests.yml").read_text()),
    )
    jobs = cast("dict[str, dict[str, object]]", document["jobs"])
    for name in ("unity-tests", "unity-tests-single-threaded"):
        steps = cast("list[dict[str, object]]", jobs[name]["steps"])
        controller = next(step for step in steps if step.get("id") == "run_modes")
        assert "github.event_name != 'workflow_dispatch'" in str(controller["if"])
        assert "steps.unity_lock.outputs.acquired == 'true'" in str(controller["if"])
        controller_environment = cast("dict[str, object]", controller["env"])
        profile = (
            "core-assembly-profiles"
            if name.endswith("single-threaded")
            else "integration-assembly-profiles"
        )
        assert (
            controller_environment["UH_PARALLEL_ASSEMBLY_PROFILES"]
            == "${{ needs.matrix-config.outputs." + profile + " }}"
        )
        assert controller_environment["GIT_CONFIG_GLOBAL"] == "${{ runner.temp }}/uh-gitconfig"
        program = str(controller["run"])
        assert program.count("./scripts/unity/assert-no-active-unity-editor.ps1") == 2
        assert (
            "--single-threaded" in program
            if name.endswith("single-threaded")
            else "--single-threaded" not in program
        )
        for step in steps:
            if step.get("id") not in (
                "run_editmode",
                "run_playmode",
                "run_standalone",
                "run_proto_only",
            ):
                continue
            environment = cast("dict[str, object]", step["env"])
            assert environment["UH_MODE_CONTROLLER_OUTCOME"] == "${{ steps.run_modes.outcome }}"
            mode = str(step["id"]).removeprefix("run_")
            assert environment["UH_MODE_OUTCOME"] == "${{ steps.run_modes.outputs." + mode + " }}"
            body = str(step["run"])
            assert (
                "$env:UH_MODE_CONTROLLER_OUTCOME -cne 'success' -or $env:UH_MODE_OUTCOME -cne 'success'"
                in body
            )
            assert body.index("$env:UH_CONCURRENT_RUN") < body.index(
                "./scripts/unity/run-ci-tests.ps1"
            )


def seed_repository(path: Path) -> None:
    path.mkdir()
    git(path, "init")
    git(path, "config", "user.name", "Fixture")
    git(path, "config", "user.email", "fixture@example.invalid")
    (path / "Source.cs").write_text("original\n", encoding="utf-8")
    shell = shutil.which("pwsh")
    assert shell is not None
    helper = str(ROOT / "scripts/git-staging-helpers.ps1").replace("'", "''")
    subprocess.run(
        [
            shell,
            "-NoProfile",
            "-Command",
            f". '{helper}'; if ((Invoke-GitAddWithRetry -Items @('.')) -ne 0) {{ exit 1 }}",
        ],
        cwd=path,
        capture_output=True,
        check=True,
        timeout=30,
    )
    git(path, "commit", "-m", "Seed fixture")


def test_owned_source_worktrees_isolate_mutations_and_refresh_the_same_commit(
    tmp_path: Path,
) -> None:
    repository = tmp_path / "repository"
    seed_repository(repository)
    modes = [Mode(name, {"RepoRoot": str(repository)}) for name in ("editmode", "playmode")]
    cache = tmp_path / "cache"
    copies = isolate_sources(modes, repository, cache)
    first = Path(str(copies[0].parameters["RepoRoot"]))
    second = Path(str(copies[1].parameters["RepoRoot"]))
    (first / "Source.cs").write_text("changed by compiler fixture\n", encoding="utf-8")
    (first / "Unexpected.cs").write_text("untracked fixture residue\n", encoding="utf-8")
    assert (second / "Source.cs").read_text(encoding="utf-8") == "original\n"
    assert (repository / "Source.cs").read_text(encoding="utf-8") == "original\n"
    assert git(repository, "status", "--porcelain") == ""
    refreshed = isolate_sources(modes, repository, cache)
    assert [item.parameters["RepoRoot"] for item in refreshed] == [
        item.parameters["RepoRoot"] for item in copies
    ]
    assert (first / "Source.cs").read_text(encoding="utf-8") == "original\n"
    assert not (first / "Unexpected.cs").exists()
    assert git(first, "rev-parse", "HEAD") == git(repository, "rev-parse", "HEAD")


@pytest.mark.parametrize("damage", ["unowned", "wrong owner", "nested checkout"])
def test_unknown_source_paths_are_preserved(tmp_path: Path, damage: str) -> None:
    repository = tmp_path / "repository"
    seed_repository(repository)
    mode = Mode("editmode", {"RepoRoot": str(repository)})
    cache = repository / "nested" if damage == "nested checkout" else tmp_path / "cache"
    destination = cache / "editmode"
    destination.mkdir(parents=True)
    canary = destination / "Canary.cs"
    canary.write_text("preserve me\n", encoding="utf-8")
    if damage == "wrong owner":
        (cache / ".editmode.owner.json").write_text('{"SchemaVersion":2}', encoding="utf-8")
    with pytest.raises(ValueError, match=r"ownership|no ownership|outside"):
        isolate_sources([mode], repository, cache)
    assert canary.read_text(encoding="utf-8") == "preserve me\n"


def test_complete_cli_uses_isolated_sources_and_publishes_all_four_modes(tmp_path: Path) -> None:
    _modes, _preparation, environment, _shell = make_control(tmp_path)
    repository = tmp_path / "repository"
    seed_repository(repository)
    runner = repository / "scripts/unity/run-ci-tests.ps1"
    runner.parent.mkdir(parents=True)
    runner.write_text(
        Path(environment["UH_MODE_RUNNER"]).read_text(encoding="utf-8"), encoding="utf-8"
    )
    helper = str(ROOT / "scripts/git-staging-helpers.ps1").replace("'", "''")
    shell = shutil.which("pwsh")
    assert shell is not None
    subprocess.run(
        [
            shell,
            "-NoProfile",
            "-Command",
            f". '{helper}'; if ((Invoke-GitAddWithRetry -Items @('.')) -ne 0) {{ exit 1 }}",
        ],
        cwd=repository,
        capture_output=True,
        check=True,
        timeout=30,
    )
    git(repository, "commit", "-m", "Add labeled worker fixture")
    private = tmp_path / "private"
    private.mkdir()
    output = tmp_path / "outputs"
    environment.update(
        RUNNER_TEMP=str(private),
        RUNNER_WORKSPACE=str(tmp_path / "workspace"),
        GITHUB_OUTPUT=str(output),
        UH_PARALLEL_SELECTED_MODES=json.dumps(list(PROFILES)),
        UH_PARALLEL_ASSEMBLY_PROFILES=json.dumps(PROFILES),
    )
    wrapper = tmp_path / "cli-wrapper.py"
    wrapper.write_text(
        "import sys\nfrom pathlib import Path\n"
        "sys.path.insert(0, "
        + repr(str(ROOT / "scripts/unity"))
        + ")\n"
        + "import run_ci_modes\nrun_ci_modes.ROOT = Path("
        + repr(str(repository))
        + ")\n"
        + "raise SystemExit(run_ci_modes.main())\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [sys.executable, str(wrapper), "--version", "6000.6.0f1"],
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=40,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert (
        output.read_text(encoding="utf-8")
        == "editmode=success\nplaymode=success\nstandalone=success\nproto_only=success\n"
    )
    artifact_paths = list((repository / ".artifacts/unity").glob("*/fixture-options.json"))
    assert len(artifact_paths) == 4
    inputs = [json.loads(path.read_text(encoding="utf-8")) for path in artifact_paths]
    assert len({item["RepoRoot"] for item in inputs}) == 4
    assert str(repository) not in {item["RepoRoot"] for item in inputs}
    receipt = json.loads(
        (repository / ".artifacts/unity/6000.6.0f1-editmode/mode-timings.json").read_text(
            encoding="utf-8"
        )
    )
    assert receipt["ControllerCompleted"] is True
    assert len(receipt["Workers"]) == 4
    assert receipt["ElapsedSeconds"] >= max(item["ElapsedSeconds"] for item in receipt["Workers"])
