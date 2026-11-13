"""Preserve the original comparison checker findings and safe rewrites."""

from __future__ import annotations

import json
import os
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
import lint_comparison_direction as lint


class ExpectedViolation(TypedDict):
    operator: str
    line: int
    column: int


class FrozenCase(TypedDict):
    name: str
    source: str
    violations: list[ExpectedViolation]
    reasons: list[str | None]
    fixed: str


SAFETY_REFUSALS = {
    "a plain greater-than",
    "a greater-or-equal",
    "a comparison after a less-than",
    "an indexer comparison",
    "a comparison in an interpolation hole",
    "both operators on one line",
    "control-34",
    "control-35",
    "control-37",
    "control-38",
    "control-45",
    "control-46",
    "control-47",
    "control-48",
    "control-49",
    "control-50",
    "control-51",
    "control-52",
    "control-53",
    "control-54",
    "control-55",
}
ANGLE_REFUSALS = {
    "a comparison beside a generic": "operand contains generic arguments",
    "control-41": "operand contains a shift or another comparison",
    "control-42": "operand contains a shift or another comparison",
}


def original_cases() -> list[FrozenCase]:
    fixture = Path(__file__).parent / "fixtures/comparison-direction-original.json"
    data = cast("dict[str, object]", json.loads(fixture.read_text(encoding="utf-8")))
    return cast("list[FrozenCase]", data["cases"])


@pytest.mark.parametrize("case", original_cases(), ids=lambda case: case["name"])
def test_original_findings_and_rewrites(case: FrozenCase) -> None:
    source = case["source"]
    tokens = lint.tokenize(source)
    generic = lint.mark_generics(tokens)
    violations = lint.find_violations(tokens, generic)
    assert [
        {"operator": value.operator, "line": value.line, "column": value.column}
        for value in violations
    ] == case["violations"]
    plans = [lint.plan_fix(source, tokens, generic, value) for value in violations]
    if case["name"] in SAFETY_REFUSALS or case["name"] in ANGLE_REFUSALS:
        reason = ANGLE_REFUSALS.get(
            case["name"], "operand independence requires a literal constant"
        )
        assert plans
        assert all(plan.replacement is None and plan.reason == reason for plan in plans)
        return
    assert [plan.reason for plan in plans] == case["reasons"]
    for plan in sorted(plans, key=lambda value: value.start, reverse=True):
        if plan.replacement is not None:
            source = source[: plan.start] + plan.replacement + source[plan.end :]
    assert source == case["fixed"]


def test_empty_scan_refuses_success(
    tmp_path: Path, monkeypatch: MonkeyPatch, capsys: CaptureFixture[str]
) -> None:
    monkeypatch.setenv("COMPARISON_DIRECTION_ROOTS", str(tmp_path))
    assert lint.main([]) == 1
    assert "checked nothing" in capsys.readouterr().err


@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_red_fix_green_preserves_newlines_and_reports(
    tmp_path: Path, monkeypatch: MonkeyPatch, capsys: CaptureFixture[str], newline: str
) -> None:
    file = tmp_path / "Sample.cs"
    source = f"class Sample {{ bool M(int a) => a > 0; }}{newline}"
    file.write_bytes(source.encode())
    monkeypatch.setenv("COMPARISON_DIRECTION_ROOTS", str(tmp_path))
    assert lint.main([]) == 1
    assert "Sample.cs:1:" in capsys.readouterr().err
    assert lint.main(["--fix"]) == 0
    assert file.read_bytes() == source.replace("a > 0", "0 < a").encode()
    assert "rewrote 1 comparison(s) across 1 file(s)" in capsys.readouterr().out
    assert lint.main(["--verbose"]) == 0
    assert "1 file(s) clean" in capsys.readouterr().out


def test_refused_fix_keeps_source_and_explains_why(
    tmp_path: Path, capsys: CaptureFixture[str]
) -> None:
    file = tmp_path / "Calls.cs"
    source = "bool M() => Next() > Peek();\n"
    file.write_text(source, encoding="utf-8")
    assert lint.main([str(file), "--fix"]) == 1
    assert "operand independence" in capsys.readouterr().err
    assert file.read_text(encoding="utf-8") == source


def test_scan_is_sorted_and_omits_build_outputs(tmp_path: Path) -> None:
    for name in (
        "z.cs",
        "a.cs",
        "ignored.txt",
        "bin/Generated.cs",
        "obj/Generated.cs",
        "nested/b.cs",
    ):
        file = tmp_path / name
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text("// source\n", encoding="utf-8")
    assert [file.relative_to(tmp_path).as_posix() for file in lint.collect_cs_files(tmp_path)] == [
        "a.cs",
        "nested/b.cs",
        "z.cs",
    ]


def test_vendored_sources_are_excluded(tmp_path: Path, monkeypatch: MonkeyPatch) -> None:
    monkeypatch.setattr(lint, "ROOT", tmp_path)
    file = tmp_path / "Runtime/Utils/SevenZip/Vendor.cs"
    file.parent.mkdir(parents=True)
    file.write_text("a > b", encoding="utf-8")
    owned = tmp_path / "Runtime/Owned.cs"
    owned.write_text("a > b", encoding="utf-8")
    assert lint.collect_cs_files(tmp_path / "Runtime") == [owned]


def test_missing_roots_do_not_hide_a_valid_root(
    tmp_path: Path, monkeypatch: MonkeyPatch, capsys: CaptureFixture[str]
) -> None:
    source = tmp_path / "Clean.cs"
    source.write_text("class Clean {}", encoding="utf-8")
    monkeypatch.setenv(
        "COMPARISON_DIRECTION_ROOTS",
        os.pathsep.join((str(tmp_path / "absent"), str(tmp_path))),
    )
    assert lint.main(["--verbose"]) == 0
    assert "1 file(s) clean" in capsys.readouterr().out


@pytest.mark.parametrize("launcher", ["python", "node"])
def test_actual_cli_refuses_missing_sources(tmp_path: Path, launcher: str) -> None:
    if launcher == "node":
        node = shutil.which("node")
        assert node is not None
        command = [node, str(lint.ROOT / "scripts/lint-comparison-direction.js")]
    else:
        command = [
            sys.executable,
            str(lint.ROOT / "scripts/lint_comparison_direction.py"),
        ]
    result = subprocess.run(
        [*command, str(tmp_path / "missing.cs")],
        cwd=lint.ROOT,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert result.returncode != 0
    assert "missing.cs" in result.stderr


def test_actual_python_cli_red_fix_green(tmp_path: Path) -> None:
    file = tmp_path / "Sample.cs"
    file.write_text("if (left >= 0) {}\n", encoding="utf-8")
    command = [
        sys.executable,
        str(lint.ROOT / "scripts/lint_comparison_direction.py"),
        str(file),
    ]
    red = subprocess.run(command, capture_output=True, text=True, check=False, timeout=30)
    assert red.returncode == 1
    assert "Sample.cs:1:10:" in red.stderr
    fixed = subprocess.run(
        [*command, "--fix"], capture_output=True, text=True, check=False, timeout=30
    )
    assert fixed.returncode == 0
    assert file.read_text(encoding="utf-8") == "if (0 <= left) {}\n"
    green = subprocess.run(
        [*command, "--verbose"], capture_output=True, text=True, check=False, timeout=30
    )
    assert green.returncode == 0
    assert "1 file(s) clean" in green.stdout


@pytest.mark.parametrize("whitespace", ["\ufeff", "\u00a0", "\u2028", "\u2029", "\u3000"])
def test_ecmascript_whitespace_preserves_physical_lines_and_columns(
    whitespace: str,
) -> None:
    tokens = lint.tokenize(f"{whitespace}if (a > b) {{}}")
    findings = lint.find_violations(tokens, lint.mark_generics(tokens))
    assert [(value.line, value.column) for value in findings] == [(1, 8)]
    assert tokens[0].text == "if"


def test_python_only_whitespace_remains_a_csharp_token() -> None:
    tokens = lint.tokenize("\u001c if (a > b) {}")
    assert tokens[0].text == "\u001c"


def test_verbatim_identifiers_are_not_keywords() -> None:
    tokens = lint.tokenize("@return > @if")
    assert [token.kind for token in tokens] == ["identifier", "angle", "identifier"]
    assert len(lint.find_violations(tokens, lint.mark_generics(tokens))) == 1


def test_legacy_test_launcher_forwards_selection(tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [
            node,
            str(lint.ROOT / "scripts/tests/test-lint-comparison-direction.js"),
            "-k",
            "test_verbatim_identifiers_are_not_keywords",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout


@pytest.mark.parametrize(
    "source",
    [
        "if (Comparer<int>.Default.Compare(a, b) > 0) {}",
        "if (a >> 2 > b) {}",
        "if (a > b << 2) {}",
        "if (x > x++) {}",
        "if (x++ > x) {}",
        "if (x > ChangeX()) {}",
        "if (ReadAndChangeX() > x) {}",
        "if (obj.Value > other.Value) {}",
        "if (items[i] >= items[j]) {}",
        "if (a + b > c * d) {}",
        'return $"{a>b}".Length > 0;',
        'return $"{a>0}".Length > 0;',
    ],
)
def test_unsafe_rewrite_is_refused_without_modifying_source(
    source: str, tmp_path: Path, capsys: CaptureFixture[str]
) -> None:
    file = tmp_path / "Unsafe.cs"
    file.write_text(source, encoding="utf-8")
    assert lint.main([str(file), "--fix"]) == 1
    assert file.read_text(encoding="utf-8") == source
    assert "rewrote 0 comparison(s)" in capsys.readouterr().out


def test_disjoint_fix_does_not_apply_nested_overlaps(
    tmp_path: Path, capsys: CaptureFixture[str]
) -> None:
    source = 'if (n>0) {} return $"{a>0}".Length > 0;'
    file = tmp_path / "Mixed.cs"
    file.write_text(source, encoding="utf-8")
    assert lint.main([str(file), "--fix"]) == 1
    assert file.read_text(encoding="utf-8") == source.replace("n>0", "0 < n")
    output = capsys.readouterr()
    assert "rewrote 1 comparison(s)" in output.out
    assert output.err.count("overlapping rewrite plans") == 2


@pytest.mark.parametrize(
    ("spans", "rejected"),
    [
        ([(0, 2), (2, 4)], set[int]()),
        ([(0, 4), (1, 2), (3, 5)], {0, 1, 2}),
        ([(0, 2), (1, 3), (2, 4)], {0, 1, 2}),
        ([(5, 10), (0, 8), (20, 30)], {0, 1}),
        ([(0, 2), (0, 2)], {0, 1}),
    ],
)
def test_overlapping_plan_groups_are_all_rejected(
    spans: list[tuple[int, int]], rejected: set[int]
) -> None:
    plans = [lint.Plan(start, end, "replacement") for start, end in spans]
    assert lint.overlapping_plans(plans) == rejected


def test_historical_safety_classifications_cover_existing_observations() -> None:
    assert SAFETY_REFUSALS | set(ANGLE_REFUSALS) <= {case["name"] for case in original_cases()}
