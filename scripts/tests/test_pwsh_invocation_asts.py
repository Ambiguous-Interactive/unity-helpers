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
    finding = policy.start_process_violation("scripts/launch.ps1", command, [command["text"]])
    if reported:
        assert finding is not None
        assert finding["code"] == "PWS006"
        assert finding["message"].startswith("Start-Process -ArG is handed")
    else:
        assert finding is None


@pytest.mark.parametrize(
    ("lines", "allowed"),
    [
        (
            [
                "# lint-pwsh-invocations: allow-start-process-argument-list fixed flags",
                "# intro",
                "run",
            ],
            True,
        ),
        (
            ["# lint-pwsh-invocations: allow-start-process-argument-list fixed flags", "", "run"],
            False,
        ),
        (["# lint-pwsh-invocations: allow-start-process-argument-list", "run"], False),
        (["run # LINT-PWSH-INVOCATIONS: ALLOW-START-PROCESS-ARGUMENT-LIST fixed flags"], True),
    ],
)
def test_opt_out_requires_attached_rationale(lines: list[str], allowed: bool) -> None:
    assert policy.allow_start_process(len(lines), lines) is allowed


@pytest.mark.parametrize(
    ("sibling", "positional"), [(False, False), (True, False), (False, True), (True, True)]
)
def test_array_rules_require_both_guards(sibling: bool, positional: bool) -> None:
    parameters: list[policy.ParameterFact] = [
        {"name": "Paths", "array_type": True, "attributes": []},
        {"name": "Output", "array_type": False, "attributes": []},
    ]
    if sibling:
        parameters.append(
            {
                "name": "Remaining",
                "array_type": True,
                "attributes": ["[Parameter(ValueFromRemainingArguments = $true)]"],
            }
        )
    block: policy.ParameterBlockFact = {
        "line": 2,
        "text": "param(...",
        "attributes": ["[CmdletBinding(PositionalBinding = $false)]"] if positional else [],
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


def test_empty_ast_list_returns_one_json_array() -> None:
    result = parse_input([])
    assert (result.returncode, result.stdout, result.stderr) == (0, b"[]\n", b"")


def test_native_cli_reads_type_facts_and_preserves_diagnostic_order(tmp_path: Path) -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    root = tmp_path / "PowerShell fixture [space]"
    for relative in (
        LINTER,
        "scripts/pwsh_invocation_asts.py",
        "scripts/python~/pyproject.toml",
        "scripts/python~/uv.lock",
    ):
        fixture = root / relative
        fixture.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, fixture)
    input_path = root / "scripts/unicode-\u03bb config.ps1"
    contents = b"param([int[]]$Paths)\n\nsaps -FilePath 'synthetic' -Arg:$arguments\n"
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
        timeout=50,
    )
    assert result.returncode == 1
    assert result.stderr == b""
    output = result.stdout.decode("utf-8")
    start = output.index("scripts/unicode-\u03bb config.ps1:3: PWS006 Start-Process -Arg ")
    stop = output.index(
        "scripts/unicode-\u03bb config.ps1:1: PWS005 declares array parameter(s) Paths"
    )
    assert start < stop
    for finding in (".githooks/pre-commit:1", ".github/workflows/native.yml:2"):
        assert output.index(f"{finding}: PWS001") < start
    assert "4 violation(s) found" in output
    assert input_path.read_bytes() == contents
