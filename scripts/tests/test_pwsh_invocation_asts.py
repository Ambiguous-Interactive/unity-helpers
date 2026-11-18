"""Check typed AST policy and the real PowerShell launcher boundary."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pwsh_invocation_asts as policy

ROOT = Path(__file__).resolve().parents[2]
LINTER = "scripts/lint-pwsh-invocations.ps1"


def parse_input(payload: object) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        [sys.executable, str(ROOT / "scripts/pwsh_invocation_asts.py")],
        input=json.dumps(payload).encode("utf-8"),
        capture_output=True,
        check=False,
        timeout=15,
    )


@pytest.mark.parametrize(
    ("kind", "reported"),
    [
        ("ArrayLiteralAst", True),
        ("ArrayExpressionAst", True),
        ("VariableExpressionAst", True),
        ("StringConstantExpressionAst", False),
        ("CommandParameterAst", False),
    ],
)
def test_only_joined_argument_shapes_are_reported(kind: str, reported: bool) -> None:
    command: policy.CommandFact = {
        "line": 1,
        "text": "saps -Arg: value",
        "arguments": [
            {"name": "FilePath", "kind": kind},
            {"name": "ArG", "kind": kind},
            {"name": "ArgumentList", "kind": kind},
        ],
    }
    finding = policy.start_process_violation("scripts/launch.ps1", command, set(), set())
    if reported:
        assert finding is not None
        assert finding["code"] == "PWS006"
        assert finding["message"].startswith("Start-Process -ArG is handed")
    else:
        assert finding is None


@pytest.mark.parametrize(
    ("line", "comments", "markers", "allowed"),
    [
        (3, {1, 2}, {1}, True),
        (3, {1}, {1}, False),
        (2, {1}, set[int](), False),
        (1, set[int](), {1}, True),
    ],
)
def test_opt_out_requires_attached_rationale(
    line: int, comments: set[int], markers: set[int], allowed: bool
) -> None:
    assert policy.allow_start_process(line, comments, markers) is allowed


@pytest.mark.parametrize(
    ("sibling", "positional"), [(False, False), (True, False), (False, True), (True, True)]
)
def test_array_rules_require_both_guards(sibling: bool, positional: bool) -> None:
    parameters: list[policy.ParameterFact] = [
        {"name": "Paths", "array_type": True, "catches_remaining": False},
        {"name": "Output", "array_type": False, "catches_remaining": False},
    ]
    if sibling:
        parameters.append(
            {
                "name": "Remaining",
                "array_type": True,
                "catches_remaining": True,
            }
        )
    block: policy.ParameterBlockFact = {
        "line": 2,
        "text": "param(...",
        "positional_disabled": positional,
        "parameters": parameters,
    }
    finding = policy.array_parameter_violation("scripts/tool.ps1", block)
    if sibling and positional:
        assert finding is None
        return
    assert finding is not None
    assert finding["line"] == 2
    assert finding["code"] == "PWS005"
    assert "declares array parameter(s) Paths but is missing" in finding["message"]
    assert ("missing a [Parameter" in finding["message"]) is not sibling
    assert ("[CmdletBinding(PositionalBinding = $false)]" in finding["message"]) is not positional


def test_invalid_receipt_is_a_failure() -> None:
    result = parse_input([{"path": "scripts/missing.ps1"}])
    assert result.returncode == 1
    assert result.stdout == b""
    assert b"Invalid AST input" in result.stderr


@pytest.mark.parametrize("invalid", [False, "false"])
def test_batch_validates_boolean_facts_before_classifying(invalid: bool | str) -> None:
    empty: list[object] = []
    result = parse_input(
        [
            {
                "path": path,
                "error": None,
                "commands": empty,
                "comment_lines": empty,
                "marker_lines": empty,
                "block": {
                    "line": 1,
                    "text": "param(...)",
                    "positional_disabled": True,
                    "parameters": [
                        {"name": "Paths", "array_type": True, "catches_remaining": False},
                        {"name": "Trailing", "array_type": False, "catches_remaining": remaining},
                    ],
                },
            }
            for path, remaining in (
                ("scripts/first.ps1", False),
                ("scripts/second.ps1", invalid),
            )
        ]
    )
    if isinstance(invalid, str):
        assert result.returncode == 1
        assert result.stdout == b""
        assert b"Expected AST field catches_remaining to be bool" in result.stderr
    else:
        assert result.returncode == 0
        assert result.stdout.count(b'"code":"PWS005"') == 2
        assert b'"path":"scripts/first.ps1"' in result.stdout
        assert b'"path":"scripts/second.ps1"' in result.stdout
        assert result.stderr == b""


def test_empty_ast_list_returns_one_json_array() -> None:
    result = parse_input([])
    assert (result.returncode, result.stdout, result.stderr) == (0, b"[]\n", b"")


def copy_native_linter(root: Path) -> None:
    for relative in (
        LINTER,
        "scripts/pwsh_invocation_asts.py",
        "scripts/python~/pyproject.toml",
        "scripts/python~/uv.lock",
    ):
        fixture = root / relative
        fixture.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, fixture)


def test_native_cli_reads_type_facts_and_preserves_diagnostic_order(tmp_path: Path) -> None:
    pwsh = shutil.which("pwsh")
    uv = shutil.which("uv")
    assert pwsh is not None
    assert uv is not None
    shadow = tmp_path / "second uv application"
    shadow.mkdir()
    shutil.copy2(uv, shadow / Path(uv).name)
    root = tmp_path / "PowerShell fixture [space]"
    copy_native_linter(root)
    input_path = root / "scripts/unicode-\u03bb config.ps1"
    contents = (
        (
            "param([int[]]$Paths)\n\nsaps -FilePath 'synthetic' -Arg:$arguments\n"
            "# lint-pwsh-invocations: allow-start-process-argument-list\x1cfixed flags\n"
            "saps -FilePath 'synthetic' -Arg:$arguments\n"
            "# lint-pwsh-invocations: allow-start-process-argument-list\u2003fixed flags\n"
            "saps -FilePath 'synthetic' -Arg:$arguments\n"
        )
        .replace("\n", "\r\n")
        .encode("utf-8-sig")
    )
    input_path.write_bytes(contents)
    for relative, invocation in (
        (".githooks/pre-commit", b"pwsh -File scripts/tool.ps1 -- payload\n"),
        (
            ".github/workflows/native.yml",
            b"steps:\n  - run: pwsh -File scripts/tool.ps1 -- payload\n",
        ),
    ):
        scanned_path = root / relative
        scanned_path.parent.mkdir(parents=True, exist_ok=True)
        scanned_path.write_bytes(invocation)
    command = (
        "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); "
        "& $env:PWS_AST_FIXTURE_LINTER -VerboseOutput; exit $LASTEXITCODE"
    )
    result = subprocess.run(
        [pwsh, "-NoProfile", "-Command", command],
        cwd=tmp_path,
        env={
            **os.environ,
            "NO_COLOR": "1",
            "PATH": os.environ["PATH"] + os.pathsep + str(shadow),
            "PWS_AST_FIXTURE_LINTER": str(root / LINTER),
            "UV_PROJECT_ENVIRONMENT": str(tmp_path / "locked AST test environment"),
        },
        check=False,
        capture_output=True,
        timeout=50,
    )
    assert result.returncode == 1
    assert result.stderr == b""
    output = result.stdout.decode("utf-8")
    start = output.index("scripts/unicode-\u03bb config.ps1:3: PWS006 Start-Process -Arg ")
    stop = output.index(
        "scripts/unicode-\u03bb config.ps1:1: PWS005 declares array parameter(s) Paths"
    )
    second = output.index("scripts/unicode-\u03bb config.ps1:5: PWS006 Start-Process -Arg ")
    assert start < second < stop
    assert "scripts/unicode-\u03bb config.ps1:7: PWS006" not in output
    for finding in (".githooks/pre-commit:1", ".github/workflows/native.yml:2"):
        assert output.index(f"{finding}: PWS001") < start
    assert "5 violation(s) found" in output
    assert input_path.read_bytes() == contents


def test_native_cli_preserves_findings_and_uv_error_on_early_child_exit(tmp_path: Path) -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    root = tmp_path / "PowerShell failing bridge"
    copy_native_linter(root)
    (root / ".githooks").mkdir()
    (root / ".githooks/pre-commit").write_bytes(b"pwsh -File scripts/tool.ps1 -- payload\n")
    # Exceed the pipe buffer while uv rejects the fixture's malformed lockfile.
    (root / "scripts/large.ps1").write_bytes(b"saps -FilePath 'synthetic' -Arg:$arguments\n" * 2400)
    (root / "scripts/python~/uv.lock").write_bytes(b"invalid unclosed TOML = {\n")
    result = subprocess.run(
        [pwsh, "-NoProfile", "-File", str(root / LINTER), "-VerboseOutput"],
        cwd=tmp_path,
        env={
            **os.environ,
            "NO_COLOR": "1",
            "UV_PROJECT_ENVIRONMENT": str(tmp_path / "locked AST test environment"),
        },
        check=False,
        capture_output=True,
        timeout=60,
    )
    assert result.returncode == 1
    assert b".githooks/pre-commit:1: PWS001" in result.stdout
    assert b"OK: No pwsh" not in result.stdout
    assert b"AST checks failed: PowerShell AST policy failed (exit" in result.stderr
    assert b"uv.lock" in result.stderr
