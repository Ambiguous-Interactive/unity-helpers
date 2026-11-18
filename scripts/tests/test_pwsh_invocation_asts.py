"""Check typed invocation policy and the real PowerShell launcher boundary."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pwsh_invocation_asts as policy
from pwsh_invocation_text import scan_source

ROOT = Path(__file__).resolve().parents[2]
LINTER = "scripts/lint-pwsh-invocations.ps1"


def parse_input(payload: object, *options: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        [sys.executable, str(ROOT / "scripts/pwsh_invocation_asts.py"), *options],
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
        findings = cast("list[policy.Violation]", json.loads(result.stdout))
        assert [(finding["path"], finding["code"]) for finding in findings] == [
            ("scripts/first.ps1", "PWS005"),
            ("scripts/second.ps1", "PWS005"),
        ]
        assert result.stderr == b""


def test_empty_ast_list_returns_one_json_array() -> None:
    result = parse_input([])
    assert (result.returncode, result.stdout, result.stderr) == (0, b"[]\n", b"")


def copy_native_linter(root: Path) -> None:
    for relative in (
        LINTER,
        "scripts/pwsh_invocation_asts.py",
        "scripts/pwsh_invocation_text.py",
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


def test_native_cli_reports_uv_error_on_early_child_exit(tmp_path: Path) -> None:
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
    assert b".githooks/pre-commit:1: PWS001" not in result.stdout
    assert b"OK: No pwsh" not in result.stdout
    assert b"Checks failed: PowerShell invocation policy failed (exit" in result.stderr
    assert b"uv.lock" in result.stderr


def test_failed_ast_validation_still_reports_completed_text_checks() -> None:
    result = parse_input(
        {
            "sources": [{"path": "tool.sh", "lines": ["pwsh -File tool.ps1 -- one"]}],
            "ast": [{"path": "scripts/incomplete.ps1"}],
            "culture": "",
        },
        "--include-text",
    )
    assert result.returncode == 0
    assert not result.stderr
    report = cast("policy.InvocationReport", json.loads(result.stdout))
    assert [(entry["line"], entry["code"]) for entry in report["findings"]] == [(1, "PWS001")]
    assert report["ast_error"] == "Invalid AST input: 'error'"


@pytest.mark.parametrize(
    ("path", "lines", "expected"),
    [
        (
            ".github/workflows/native.yaml",
            ["run: >-", "  powershell -f", "  scripts/build.ps1", "  -- payload"],
            [(1, "PWS001", "powershell -f scripts/build.ps1 -- payload")],
        ),
        (
            "scripts/tests/check.ps1",
            ["& $target -- payload", "Write-Host 'pwsh -File tool.ps1 -- payload'"],
            [(1, "PWS002", "& $target -- payload")],
        ),
        (
            "scripts/top.ps1",
            ["# lint-pwsh-invocations: allow-subprocess-pwsh\x1creason", "pwsh -f $child"],
            [(2, "PWS003", "pwsh -f $child")],
        ),
        (
            "scripts/top.ps1",
            ["# lint-pwsh-invocations: allow-subprocess-pwsh\u2003reason", "pwsh -f $child"],
            [],
        ),
        (
            "scripts/nested/run.ps1",
            [
                "$Hook = Join-Path '.githooks' 'pre-push'",
                "pwsh -f $HOOK",
                "pwsh -f $OTHER",
                "$Other = '.githooks/pre-commit'",
                "pwsh -f $other",
            ],
            [(2, "PWS004", "pwsh -f $HOOK"), (5, "PWS004", "pwsh -f $other")],
        ),
        (
            "scripts/run.sh",
            ["pwsh\x1c-f tool.ps1 -- arg", "pwsh\u2003-f tool.ps1 -- arg"],
            [(2, "PWS001", "pwsh\u2003-f tool.ps1 -- arg")],
        ),
        (
            "scripts/run.sh",
            [
                'echo\u0301 "pwsh -File tool.ps1 -- arg"',
                'echo\u200d "pwsh -File tool.ps1 -- arg"',
                'echo\u00b2 "pwsh -File tool.ps1 -- arg"',
                'pwsh -Command "Write-Host\u0301 -File tool.ps1 -- arg"',
                'pwsh -Command "Write-Host\u00b2 -File tool.ps1 -- arg"',
            ],
            [
                (1, "PWS001", 'echo\u0301 "pwsh -File tool.ps1 -- arg"'),
                (2, "PWS001", 'echo\u200d "pwsh -File tool.ps1 -- arg"'),
                (4, "PWS001", 'pwsh -Command "Write-Host\u0301 -File tool.ps1 -- arg"'),
            ],
        ),
        (
            "scripts/nested/assign.ps1",
            [
                "$first = Join-Path\u0301 '.githooks' 'pre-push'",
                "pwsh -File $first",
                "$second = Join-Path\u00b2 '.githooks' 'pre-push'",
                "pwsh -File $second",
            ],
            [(4, "PWS004", "pwsh -File $second")],
        ),
    ],
)
def test_text_rules_keep_physical_positions_and_content(
    path: str, lines: list[str], expected: list[tuple[int, str, str]]
) -> None:
    findings = scan_source(path, lines)
    assert [(item["line"], item["code"], item["content"]) for item in findings] == expected


@pytest.mark.parametrize(
    ("culture", "shell_lines"),
    [
        ("", [4, 5, 7]),
        ("en-US", [2, 4, 5, 7, 9]),
        ("tr-TR", [2, 4, 5, 7, 10]),
    ],
)
def test_native_cli_preserves_legacy_culture_and_word_matching(
    tmp_path: Path, culture: str, shell_lines: list[int]
) -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    copy_native_linter(tmp_path)
    (tmp_path / "script.sh").write_text(
        "power\u017fhell -f tool.ps1 -- arg\n"
        "pwsh -F\u0130LE tool.ps1 -- arg\n"
        "pwsh -f\u0131le tool.ps1 -- arg\n"
        "pwsh -FiLe tool.ps1 -- arg\n"
        "pwsh -File .githoo\u212as/pre-push\n"
        "pwsh -File .githook\u017f/pre-push\n"
        "pwsh -File .githooks/pre-puSh\n"
        '"${POWERSHELL_\u017f[@]}" tool.ps1 -- arg\n'
        '"${POWERSHELL_\u0130[@]}" tool.ps1 -- arg\n'
        '"${POWERSHELL_\u0131[@]}" tool.ps1 -- arg\n',
        encoding="utf-8",
    )
    (tmp_path / "scripts/fixture.ps1").write_text(
        "pwsh -File $\u00b2\npwsh -File $\u0301\npwsh -File $zero\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            pwsh,
            "-NoProfile",
            "-Command",
            (
                "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); "
                "[Threading.Thread]::CurrentThread.CurrentCulture = "
                "[Globalization.CultureInfo]::GetCultureInfo([string]$env:PWS_SAMPLE_CULTURE); "
                "& ./scripts/lint-pwsh-invocations.ps1; exit $LASTEXITCODE"
            ),
        ],
        cwd=tmp_path,
        env={
            **os.environ,
            "NO_COLOR": "1",
            "PWS_SAMPLE_CULTURE": culture,
            "UV_PROJECT_ENVIRONMENT": str(tmp_path / "environment"),
        },
        check=False,
        capture_output=True,
        timeout=40,
    )
    assert result.returncode == 1
    assert result.stderr == b""
    output = result.stdout.decode("utf-8")
    assert [line.split(": ", 1)[0] for line in output.splitlines() if ": PWS00" in line] == [
        *(f"script.sh:{line}" for line in shell_lines),
        "scripts/fixture.ps1:2",
        "scripts/fixture.ps1:3",
    ]
