"""Verify pinned action declarations, step ownership and real Git object reads."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING, TypedDict, cast

import pytest

if TYPE_CHECKING:
    from _pytest.capture import CaptureFixture
    from _pytest.monkeypatch import MonkeyPatch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_lock_inputs as inputs


class FrozenCase(TypedDict):
    operation: str
    args: list[object]
    output: object


def original_cases() -> list[FrozenCase]:
    content = (Path(__file__).parent / "fixtures/build-lock-inputs-original.json").read_text(
        encoding="utf-8"
    )
    return cast("list[FrozenCase]", cast("dict[str, object]", json.loads(content))["cases"])


@pytest.mark.parametrize("case", original_cases())
def test_original_parser_observations(case: FrozenCase) -> None:
    arguments = case["args"]
    if case["operation"] == "keys":
        actual: object = inputs.collect_with_keys(
            cast("list[str]", arguments[0]),
            cast("int", arguments[1]),
            cast("int", arguments[2]),
        )
    else:
        actual = [
            list(item)
            for item in inputs.parse_action_inputs(
                cast("str", arguments[0]), cast("str", arguments[1])
            ).items()
        ]
    assert actual == case["output"]


def git(root: Path, *arguments: str) -> str:
    executable = shutil.which("git")
    assert executable is not None
    return subprocess.run(
        [
            executable,
            "-C",
            str(root),
            "-c",
            "user.name=Fixture",
            "-c",
            "user.email=fixture@example.invalid",
            "-c",
            "commit.gpgsign=false",
            "-c",
            "core.hooksPath=" + str(root / "no-hooks"),
            *arguments,
        ],
        check=True,
        capture_output=True,
        encoding="utf-8",
        timeout=20,
        env=inputs.git_environment(),
    ).stdout.strip()


def commit_action(policy: Path, content: str) -> str:
    path = policy / ".github/actions/example/action.yml"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    git(policy, "add", "--", ".github")
    git(policy, "commit", "-m", "Record fixture action")
    return git(policy, "rev-parse", "HEAD")


@pytest.fixture
def policy(tmp_path: Path) -> tuple[Path, str, str]:
    root = tmp_path / "policy"
    root.mkdir()
    git(root, "init", "--object-format=sha1", "--template=")
    first = commit_action(
        root,
        "inputs:\n  alpha:\n    required: true\n  optional:\n    required: false\n",
    )
    second = commit_action(
        root,
        "inputs:\n  beta:\n    required: true\n  fallback:\n    required: true\n    default: value\n",
    )
    return root, first, second


def write_workflow(
    root: Path, reference: str, supplied: str = "alpha", file: str = "workflow.yml"
) -> Path:
    path = root / ".github/workflows" / file
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        f"jobs:\n  job:\n    steps:\n      - uses: {inputs.ACTION_PREFIX}example@{reference} # pinned\n        with:\n          {supplied}: value\n",
        encoding="utf-8",
    )
    return path


def test_each_commit_reads_its_own_input_contract(
    tmp_path: Path, policy: tuple[Path, str, str]
) -> None:
    policy_root, first, second = policy
    root = tmp_path / "consumer"
    write_workflow(root, first)
    write_workflow(root, second, "beta", "second.yaml")
    result = inputs.validate_build_lock_action_inputs(root, policy_root)
    assert (result.call_count, result.file_count, result.version_count) == (2, 2, 2)
    assert result.failures == []


def test_missing_and_unknown_inputs_are_reported_in_order(
    tmp_path: Path, policy: tuple[Path, str, str]
) -> None:
    policy_root, first, _ = policy
    root = tmp_path / "consumer"
    write_workflow(root, first, "wrong")
    result = inputs.validate_build_lock_action_inputs(root, policy_root)
    assert result.failures == [
        f".github/workflows/workflow.yml:4: example@{first[:8]} requires input 'alpha', which this call does not pass.",
        f".github/workflows/workflow.yml:4: example@{first[:8]} declares no input 'wrong', which this call passes.",
    ]


def test_shared_pin_is_read_once(
    tmp_path: Path, policy: tuple[Path, str, str], monkeypatch: MonkeyPatch
) -> None:
    policy_root, first, _ = policy
    root = tmp_path / "consumer"
    write_workflow(root, first)
    write_workflow(root, first, file="second.yml")
    seen: list[tuple[str, str]] = []
    original = inputs.read_action_at_ref

    def read(policy: Path, action: str, ref: str) -> str:
        seen.append((action, ref))
        return original(policy, action, ref)

    monkeypatch.setattr(inputs, "read_action_at_ref", read)
    assert inputs.validate_build_lock_action_inputs(root, policy_root).failures == []
    assert seen == [("example", first)]


@pytest.mark.parametrize(
    "line",
    [
        "uses: 'PREFIXexample@pin'",
        "uses: PREFIXexample",
        "uses: PREFIX@example",
        "uses: PREFIXexample@",
        "uses: other/PREFIXexample@pin",
    ],
)
def test_malformed_action_calls_are_refused(tmp_path: Path, line: str) -> None:
    path = tmp_path / "workflow.yml"
    path.write_text(line.replace("PREFIX", inputs.ACTION_PREFIX), encoding="utf-8")
    with pytest.raises(ValueError, match=r"reference (?:could not be parsed|is not pinned)"):
        inputs.collect_calls(path, "workflow.yml")


def test_comments_and_unrelated_uses_are_ignored(tmp_path: Path) -> None:
    path = tmp_path / "workflow.yml"
    path.write_text(
        f"# uses: {inputs.ACTION_PREFIX}example@pin\nuses: actions/checkout@v1\n",
        encoding="utf-8",
    )
    assert inputs.collect_calls(path, "workflow.yml") == []


@pytest.mark.parametrize("required", ["true", "'true'", '"true"', "false", '"false"'])
def test_quoted_required_flags(required: str) -> None:
    assert inputs.parse_action_inputs(
        f"inputs:\n  name:\n    required: {required}\n", "fixture"
    ) == {"name": "true" in required}


def test_unparseable_input_declaration_fails() -> None:
    with pytest.raises(ValueError, match="fixture: unparsable input declaration on line 2"):
        inputs.parse_action_inputs("inputs:\n  'quoted':\n    required: true\n", "fixture")


def test_missing_commit_explains_history_requirement(
    policy: tuple[Path, str, str],
) -> None:
    with pytest.raises(ValueError, match="full history"):
        inputs.read_action_at_ref(policy[0], "example", "0" * 40)


def test_output_bound_is_enforced(policy: tuple[Path, str, str], monkeypatch: MonkeyPatch) -> None:
    monkeypatch.setattr(inputs, "MAX_ACTION_BYTES", 8)
    with pytest.raises(ValueError, match="8-byte limit"):
        inputs.read_action_at_ref(policy[0], "example", policy[1])


def test_git_is_required(policy: tuple[Path, str, str], monkeypatch: MonkeyPatch) -> None:
    def missing(_command: str) -> None:
        return None

    monkeypatch.setattr(shutil, "which", missing)
    with pytest.raises(ValueError, match="Git is required"):
        inputs.read_action_at_ref(policy[0], "example", policy[1])


def test_git_timeouts_are_reported(policy: tuple[Path, str, str], monkeypatch: MonkeyPatch) -> None:
    def timeout(*args: object, **kwargs: object) -> subprocess.CompletedProcess[bytes]:
        del args, kwargs
        raise subprocess.TimeoutExpired("git", 30)

    monkeypatch.setattr(subprocess, "run", timeout)
    with pytest.raises(ValueError, match="full history"):
        inputs.read_action_at_ref(policy[0], "example", policy[1])


def test_local_skip_is_distinct_from_actions_failure(
    monkeypatch: MonkeyPatch, capsys: CaptureFixture[str]
) -> None:
    monkeypatch.delenv("BUILD_LOCK_POLICY_ROOT", raising=False)
    monkeypatch.delenv("GITHUB_ACTIONS", raising=False)
    assert inputs.main() == 0
    assert "SKIPPED" in capsys.readouterr().out
    monkeypatch.setenv("GITHUB_ACTIONS", "true")
    assert inputs.main() == 1
    assert "unset under GitHub Actions" in capsys.readouterr().err


def test_zero_calls_fail_main(
    tmp_path: Path,
    policy: tuple[Path, str, str],
    monkeypatch: MonkeyPatch,
    capsys: CaptureFixture[str],
) -> None:
    (tmp_path / ".github").mkdir()
    monkeypatch.setattr(inputs, "ROOT", tmp_path)
    monkeypatch.setenv("BUILD_LOCK_POLICY_ROOT", str(policy[0]))
    assert inputs.main() == 1
    assert "Found 0 pinned" in capsys.readouterr().err


def test_main_reports_success_and_failure_counts(
    tmp_path: Path,
    policy: tuple[Path, str, str],
    monkeypatch: MonkeyPatch,
    capsys: CaptureFixture[str],
) -> None:
    root, first, _ = policy
    write_workflow(tmp_path, first)
    monkeypatch.setattr(inputs, "ROOT", tmp_path)
    monkeypatch.setenv("BUILD_LOCK_POLICY_ROOT", str(root))
    assert inputs.main() == 0
    assert "1 call(s) across 1 file(s) match 1 pinned" in capsys.readouterr().out
    write_workflow(tmp_path, first, "wrong")
    assert inputs.main() == 1
    report = capsys.readouterr().err
    assert report.count("::error::") == 2
    assert "2 input contract violation(s)" in report


@pytest.mark.parametrize("launcher", ["python", "node"])
def test_actual_cli_local_skip(launcher: str) -> None:
    if launcher == "python":
        command = [sys.executable, str(inputs.ROOT / "scripts/build_lock_inputs.py")]
    else:
        node = shutil.which("node")
        assert node is not None
        command = [
            node,
            str(inputs.ROOT / "scripts/validate-build-lock-action-inputs.js"),
        ]
    environment = os.environ.copy()
    environment.pop("BUILD_LOCK_POLICY_ROOT", None)
    environment.pop("GITHUB_ACTIONS", None)
    result = subprocess.run(
        command,
        capture_output=True,
        text=True,
        env=environment,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0
    assert "SKIPPED" in result.stdout


def test_actual_cli_checks_a_complete_consumer_copy(
    tmp_path: Path, policy: tuple[Path, str, str]
) -> None:
    scripts = tmp_path / "consumer/scripts"
    scripts.mkdir(parents=True)
    for name in ("build_lock_inputs.py", "cli_encoding.py"):
        shutil.copyfile(inputs.ROOT / "scripts" / name, scripts / name)
    write_workflow(scripts.parent, policy[1])
    environment = {
        **os.environ,
        "BUILD_LOCK_POLICY_ROOT": str(policy[0]),
        "GITHUB_ACTIONS": "true",
    }
    result = subprocess.run(
        [sys.executable, str(scripts / "build_lock_inputs.py")],
        capture_output=True,
        text=True,
        env=environment,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert "1 call(s) across 1 file(s) match 1 pinned" in result.stdout


def test_inherited_git_environment_cannot_redirect_objects_or_fixture_writes(
    tmp_path: Path, policy: tuple[Path, str, str], monkeypatch: MonkeyPatch
) -> None:
    policy_root, first, second = policy
    foreign = tmp_path / "foreign"
    foreign.mkdir()
    git(foreign, "init", "--object-format=sha1", "--template=")
    original_head = commit_action(foreign, "inputs:\n  foreign:\n    required: true\n")
    original_index = (foreign / ".git/index").read_bytes()
    redirected_index = foreign / "redirected.index"
    monkeypatch.setenv("GIT_DIR", str(foreign / ".git"))
    monkeypatch.setenv("GIT_WORK_TREE", str(foreign))
    monkeypatch.setenv("GIT_INDEX_FILE", str(redirected_index))
    assert "alpha" in inputs.read_action_at_ref(policy_root, "example", first)
    assert git(policy_root, "rev-parse", "HEAD") == second
    separate = tmp_path / "separate"
    separate.mkdir()
    git(separate, "init", "--object-format=sha1", "--template=")
    commit_action(separate, "inputs:\n  separate:\n    required: false\n")
    assert git(foreign, "rev-parse", "HEAD") == original_head
    assert (foreign / ".git/index").read_bytes() == original_index
    assert not redirected_index.exists()


def test_replace_refs_cannot_change_the_pinned_action(
    policy: tuple[Path, str, str],
) -> None:
    root, first, second = policy
    git(root, "replace", first, second)
    executable = shutil.which("git")
    assert executable is not None
    environment = inputs.git_environment()
    environment.pop("GIT_NO_REPLACE_OBJECTS")
    control = subprocess.run(
        [
            executable,
            "-C",
            str(root),
            "show",
            f"{first}:.github/actions/example/action.yml",
        ],
        check=True,
        capture_output=True,
        encoding="utf-8",
        env=environment,
        timeout=20,
    )
    assert "beta" in control.stdout
    assert "alpha" in inputs.read_action_at_ref(root, "example", first)


class FrozenPin(TypedDict):
    name: str
    action: str
    files: dict[str, str] | None
    value: str | None
    error: str | None


def original_pins() -> list[FrozenPin]:
    fixture = Path(__file__).parent / "fixtures/build-lock-pin-original.json"
    content = cast("dict[str, object]", json.loads(fixture.read_text(encoding="utf-8")))
    return cast("list[FrozenPin]", content["cases"])


@pytest.mark.parametrize("case", original_pins(), ids=lambda case: case["name"])
def test_original_pin_observations(case: FrozenPin, tmp_path: Path) -> None:
    files = case["files"]
    if files is not None:
        (tmp_path / ".github").mkdir()
        for relative, content in files.items():
            file = tmp_path / ".github" / relative
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(content.encode("utf-8"))
    expected = case["error"]
    if expected is not None:
        with pytest.raises(
            ValueError, match=re.escape(expected.replace("<root>", str(tmp_path)))
        ) as observed:
            inputs.resolve_build_lock_pin(case["action"], tmp_path)
        assert str(observed.value).replace(str(tmp_path), "<root>") == expected
    else:
        assert inputs.resolve_build_lock_pin(case["action"], tmp_path) == case["value"]


@pytest.mark.parametrize(
    "arguments",
    [
        ["--resolve"],
        ["--resolve", "example", "bad"],
        ["--resolve", "example", "--bad", "path"],
    ],
)
def test_pin_usage_errors_fail(arguments: list[str], capsys: CaptureFixture[str]) -> None:
    assert inputs.main(arguments) == 2
    assert "usage:" in capsys.readouterr().err


def test_actual_resolver_cli_and_legacy_export(tmp_path: Path) -> None:
    pin = "0123456789abcdef0123456789abcdef01234567"
    write_workflow(tmp_path, pin)
    python = subprocess.run(
        [
            sys.executable,
            str(inputs.ROOT / "scripts/build_lock_inputs.py"),
            "--resolve",
            "example",
            "--root",
            str(tmp_path),
        ],
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    assert python.returncode == 0, python.stderr
    assert python.stdout == pin + "\n"
    node = shutil.which("node")
    assert node is not None
    code = "const{resolveBuildLockPin}=require('./scripts/resolve-build-lock-pin.js');console.log(resolveBuildLockPin('example',process.argv[1]));"
    legacy = subprocess.run(
        [node, "-e", code, str(tmp_path)],
        cwd=inputs.ROOT,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    assert legacy.returncode == 0, legacy.stderr
    assert legacy.stdout == python.stdout


def test_real_workflow_pin_resolves_to_a_lowercase_commit() -> None:
    pin = inputs.resolve_build_lock_pin("require-confirmed-unity-cleanup")
    assert len(pin) == 40
    assert all(character in "0123456789abcdef" for character in pin)


def test_workflow_installs_python_and_uv_before_the_pin_resolver() -> None:
    content = (inputs.ROOT / ".github/workflows/local-gates.yml").read_text(encoding="utf-8")
    assert content.index("- name: Setup Python\n") < content.index("- name: Install uv\n")
    assert content.index("- name: Install uv\n") < content.index(
        "- name: Resolve pinned central cleanup policy commit\n"
    )


def test_legacy_test_launcher_forwards_selection(tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [
            node,
            str(inputs.ROOT / "scripts/tests/test-build-lock-action-input-parser.js"),
            "-k",
            "test_real_workflow_pin_resolves_to_a_lowercase_commit",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout
