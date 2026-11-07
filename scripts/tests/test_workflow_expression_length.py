"""Exercise run-block length, discovery, and compatibility entry points."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_workflow_expression_length as lint

ROOT = Path(__file__).resolve().parents[2]


def workflow(body: str, indicator: str = "|") -> str:
    return "name: Test\njobs:\n  test:\n    steps:\n      - run: " + indicator + "\n" + body


def padding(size: int, expression: bool = True) -> str:
    first = "${{ inputs.value }}" if expression else "$env:INPUT"
    result = "          echo " + first + "\n"
    while len(result) < size:
        result += "          # padding " + "a" * 56 + "\n"
    return result.rstrip("\r\n")


def write(root: Path, text: str, relative: str = ".github/actions/test/action.yml") -> Path:
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return path


@pytest.mark.parametrize("indicator", ["|", ">", "|-", "|+", "|2", "|2-", "|-2", ">2+"])
def test_large_block_with_expression_fails(tmp_path: Path, indicator: str) -> None:
    write(tmp_path, workflow(padding(21000), indicator))
    assert lint.check(tmp_path) == 1


@pytest.mark.parametrize(
    ("size", "expression", "expected"),
    [(18000, True, 0), (20200, True, 1), (21000, False, 0), (100, True, 0)],
)
def test_original_boundaries(tmp_path: Path, size: int, expression: bool, expected: int) -> None:
    write(tmp_path, workflow(padding(size, expression)))
    assert lint.check(tmp_path) == expected


@pytest.mark.parametrize(
    "relative",
    [
        ".github/workflows/test.yml",
        ".github/workflows/test.yaml",
        ".github/actions/deep/test/action.yml",
        ".github/actions/deep/test/action.yaml",
    ],
)
def test_workflows_and_nested_actions(tmp_path: Path, relative: str) -> None:
    write(tmp_path, workflow(padding(21000)), relative)
    assert lint.check(tmp_path) == 1


def test_env_mapping_does_not_count_as_run_expression(tmp_path: Path) -> None:
    text = workflow(padding(21000, False)).replace(
        "      - run:", "      - env: {INPUT: '${{ inputs.value }}'}\n        run:"
    )
    write(tmp_path, text)
    assert lint.check(tmp_path) == 0


def test_description_run_token_is_not_a_key(tmp_path: Path) -> None:
    text = 'description: "The run: key uses ${{ inputs.value }}"\n' + workflow(
        "          echo fine"
    )
    write(tmp_path, text)
    assert lint.check(tmp_path) == 0


def test_run_text_inside_literal_is_not_a_mapping(tmp_path: Path) -> None:
    text = (
        "description: |\n  run: '${{ inputs.value }}"
        + "x" * 21000
        + "'\n"
        + workflow("          echo fine")
    )
    write(tmp_path, text)
    assert lint.check(tmp_path) == 0


def test_multiple_blocks_report_only_offending_one(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    text = workflow("          echo '${{ github.ref }}'\n      - run: |\n" + padding(21000))
    write(tmp_path, text)
    assert lint.check(tmp_path) == 1
    assert capsys.readouterr().out.count("WFL001") == 1


@pytest.mark.parametrize(("size", "expected"), [(19999, 0), (20000, 0), (20001, 1)])
def test_exact_utf16_boundary(tmp_path: Path, size: int, expected: int) -> None:
    prefix = "          echo '${{ x }}' "
    text = workflow(prefix + "x" * (size - len(prefix)))
    write(tmp_path, text)
    assert lint.check(tmp_path) == expected


def test_non_bmp_characters_count_as_two_units(tmp_path: Path) -> None:
    body = "          echo '${{ x }}' " + "😀" * 10000
    assert len(body) < lint.MAXIMUM_LENGTH
    write(tmp_path, workflow(body))
    assert lint.check(tmp_path) == 1


def test_quoted_run_key_and_inline_scalar(tmp_path: Path) -> None:
    write(tmp_path, "'run': '${{ x }}" + "x" * 20000 + "'\n")
    assert lint.check(tmp_path) == 1


def test_empty_and_no_run_files_are_safe(tmp_path: Path) -> None:
    write(tmp_path, "")
    write(
        tmp_path,
        "name: NoRun\njobs: {test: {steps: [{uses: actions/checkout@v4}]}}",
        ".github/workflows/no-run.yml",
    )
    assert lint.check(tmp_path) == 0


@pytest.mark.parametrize("text", ["run: [", "run: [echo, hello]", "run: {command: echo}"])
def test_uninspectable_yaml_fails(tmp_path: Path, text: str) -> None:
    write(tmp_path, text)
    assert lint.check(tmp_path) == 1


def test_recursive_alias_is_bounded(tmp_path: Path) -> None:
    write(tmp_path, "recursive: &self {next: *self}\nrun: echo fine\n")
    assert lint.check(tmp_path) == 0


def test_missing_and_empty_corpus_fail(tmp_path: Path) -> None:
    assert lint.main(["--repo-root", str(tmp_path / "missing")]) == 1
    assert lint.main(["--repo-root", str(tmp_path)]) == 1


def test_nested_workflow_is_not_a_github_workflow(tmp_path: Path) -> None:
    write(tmp_path, workflow(padding(21000)), ".github/workflows/nested/ignored.yml")
    write(tmp_path, workflow("          echo fine"))
    assert lint.check(tmp_path) == 0


def test_linked_input_is_refused(tmp_path: Path) -> None:
    target = write(tmp_path, workflow("          echo fine"))
    link = tmp_path / ".github/workflows/link.yml"
    link.parent.mkdir(parents=True)
    try:
        link.symlink_to(target)
    except OSError:
        pytest.skip("Creating symlinks requires host permission")
    assert lint.main(["--repo-root", str(tmp_path)]) == 1


@pytest.mark.parametrize("launcher", [False, True])
@pytest.mark.parametrize("oversized", [False, True])
def test_actual_cli(tmp_path: Path, launcher: bool, oversized: bool) -> None:
    write(tmp_path, workflow(padding(21000 if oversized else 100)))
    if launcher:
        shell = shutil.which("pwsh")
        if shell is None:
            pytest.skip("PowerShell is unavailable")
        command = [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-workflow-run-expression-length.ps1"),
            "-RepoRoot",
            str(tmp_path),
            "-VerboseOutput",
        ]
    else:
        command = [
            sys.executable,
            str(ROOT / "scripts/lint_workflow_expression_length.py"),
            "--repo-root",
            str(tmp_path),
            "--verbose",
        ]
    result = subprocess.run(
        command, cwd=tmp_path, capture_output=True, text=True, check=False, timeout=60
    )
    assert result.returncode == int(oversized), result.stdout + result.stderr
    assert ("WFL001" in result.stdout) == oversized
    assert "Scanned 1 files" in result.stdout


def test_legacy_test_launcher_runs_selected_control(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("PowerShell is unavailable")
    command = [
        shell,
        "-NoProfile",
        "-File",
        str(ROOT / "scripts/tests/test-lint-workflow-run-expression-length.ps1"),
        "-TestFilter",
        "test_original_boundaries",
    ]
    result = subprocess.run(
        command, cwd=tmp_path, capture_output=True, text=True, check=False, timeout=60
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "4 passed" in result.stdout


def test_linked_action_directory_cannot_hide_an_input(tmp_path: Path) -> None:
    write(tmp_path, workflow("          echo fine"), ".github/workflows/ok.yml")
    actual = tmp_path / "outside/action.yml"
    actual.parent.mkdir()
    actual.write_text(workflow(padding(21000)), encoding="utf-8")
    linked = tmp_path / ".github/actions/linked"
    linked.parent.mkdir(parents=True)
    try:
        linked.symlink_to(actual.parent, target_is_directory=True)
    except OSError:
        pytest.skip("Creating directory symlinks requires host permission")
    assert lint.main(["--repo-root", str(tmp_path)]) == 1


@pytest.mark.parametrize("kind", ["workflow", "job", "input"])
def test_non_command_run_mappings_are_allowed(tmp_path: Path, kind: str) -> None:
    text = workflow("          echo fine")
    if kind == "workflow":
        text = "defaults:\n  run: {shell: bash}\n" + text
    elif kind == "job":
        text = text.replace("    steps:", "    defaults:\n      run: {shell: bash}\n    steps:")
    else:
        text = (
            "inputs:\n  run: {description: command, required: false}\n"
            "runs:\n  using: composite\n  steps:\n    - run: echo fine\n      shell: bash\n"
        )
    write(tmp_path, text)
    assert lint.check(tmp_path) == 0


@pytest.mark.parametrize("command", ["{command: echo}", "[echo, hello]"])
def test_non_scalar_step_run_is_rejected(tmp_path: Path, command: str) -> None:
    write(tmp_path, "jobs: {test: {steps: [{run: " + command + "}]}}")
    assert lint.check(tmp_path) == 1


def test_shared_step_alias_is_checked_in_step_context(tmp_path: Path) -> None:
    text = "prototype: &command {run: '${{ x }}" + "x" * 20000 + "'}\n"
    text += "jobs: {test: {steps: [*command]}}\n"
    write(tmp_path, text)
    assert lint.check(tmp_path) == 1
