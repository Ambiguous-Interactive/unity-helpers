"""Verify manifest ownership, report parsing, and the hosted PractRand verdict."""

from __future__ import annotations

import copy
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts" / "random-quality"
sys.path.insert(0, str(SCRIPTS))
from evaluate_practrand import classify_report, evaluate, parse_length, read_manifest  # noqa: E402

if TYPE_CHECKING:
    from evaluate_practrand import Manifest, Result

MANIFEST_PATH = SCRIPTS / "expected-outcomes.json"
MANIFEST = read_manifest(MANIFEST_PATH)
NAMES = [generator["name"] for generator in MANIFEST["generators"]]
WIDTHS = ("32", "64")
PCG = "PcgRandom"
XORSHIFT = "XorShiftRandom"
SYSTEM = "SystemRandom"
CLEAN_LENGTH = "1 gigabyte (2^30 bytes)"
LONG_LENGTH = "32 gigabytes (2^35 bytes)"
TABLE = "  Test Name                         Raw       Processed     Evaluation"
FAIL_ROW = "  [Low1/32]BRank(12):256(1)         R= +2650  p~=  9.8e-799   FAIL !!!!!!!"


def complete_report(body: str, width: str = "32") -> str:
    return (
        "RNG_test using PractRand version 0.95\n"
        f"RNG = RNG_stdin{width}, seed = unknown\n"
        f"test set = core, folding = standard ({width} bit)\n\n"
        f"rng=RNG_stdin{width}, seed=unknown\n{body}\n\n"
    )


CLEAN = complete_report(
    f"length= {CLEAN_LENGTH}, time= 15.4 seconds\n  no anomalies in 156 test result(s)"
)
SUSPICIOUS = complete_report(
    "length= 4 kilobytes (2^12 bytes), time= 0.2 seconds\n"
    f"{TABLE}\n"
    "  DC6-9x1Bytes-1                    R=  +7.5  p =  5.6e-3   unusual\n"
    "  BCFN(2+1,13-9,T)                  R= +43.5  p =  6.7e-11   VERY SUSPICIOUS\n"
    "  ...and 16 test result(s) without anomalies"
)
FAILED = complete_report(
    f"length= 16 megabytes (2^24 bytes), time= 9.3 seconds\n{TABLE}\n{FAIL_ROW}"
)
REPORT_HEADER = CLEAN[: CLEAN.index("rng=RNG_stdin32")]
MALFORMED = [
    ("empty", ""),
    ("whitespace", " \n\n"),
    ("arbitrary", "the process exited successfully\n"),
    ("outside-table", "FAIL: could not read stdin\n"),
    ("header", REPORT_HEADER),
    ("length-only", complete_report("length= 1 gigabyte (2^30 bytes), time= 1.0 seconds")),
    ("zero-tests", CLEAN.replace("156 test result(s)", "0 test result(s)")),
    ("no-separator", CLEAN.rstrip()),
    ("nonnumeric-probability", FAILED.replace("9.8e-799", "not-a-number")),
    ("nonfinite-raw", FAILED.replace("+2650", "NaN")),
    ("overflow-raw", FAILED.replace("+2650", "1e999")),
    ("probability-range", FAILED.replace("9.8e-799", "2")),
    ("complement-range", FAILED.replace("9.8e-799", "1-2")),
    ("probability-overflow", FAILED.replace("9.8e-799", "1e999")),
    ("truncated-row", FAILED.replace("p~=  9.8e-799   FAIL !!!!!!!", "p~= ")),
    ("unfinished-block", CLEAN + "rng=RNG_stdin32, seed=unknown\n"),
    ("unfinished-length", CLEAN + "rng=RNG_stdin32, seed=unknown\nlength= 2 gigabyte"),
    ("unexpected-output", CLEAN + "error: unexpected EOF\n\n"),
    ("contradictory-width", CLEAN.replace("rng=RNG_stdin32", "rng=RNG_stdin64")),
    ("repeated-length", CLEAN + CLEAN[len(REPORT_HEADER) :]),
    ("exponent-overflow", CLEAN.replace("2^30", "2^999")),
    ("contradictory-length", CLEAN.replace("2^30", "2^29")),
    ("unparseable-length", CLEAN.replace("1 gigabyte", "a lot of data")),
    ("empty-table", complete_report(f"length= {CLEAN_LENGTH}, time= 1.0 seconds\n{TABLE}")),
]


def for_width(report: str, width: str) -> str:
    return report.replace("stdin32", f"stdin{width}").replace("(32 bit)", f"({width} bit)")


def reports_for(
    width: str, overrides: dict[str, str] | None = None, clean_length: str = LONG_LENGTH
) -> dict[str, str]:
    return {
        generator["name"]: for_width(
            FAILED
            if (overrides or {}).get(generator["name"], generator["widths"][width]["expected"])
            == "fail"
            else CLEAN.replace(CLEAN_LENGTH, clean_length),
            width,
        )
        for generator in MANIFEST["generators"]
    }


def result_for(results: list[Result], name: str) -> Result:
    return next(result for result in results if result["name"] == name)


def test_manifest_identity_and_quality() -> None:
    assert NAMES
    assert len(set(NAMES)) == len(NAMES)
    assert sorted(NAMES, key=str.lower) == NAMES
    assert MANIFEST["battery"]["name"] == "PractRand"
    assert re.fullmatch(r"[0-9a-f]{64}", MANIFEST["battery"]["sha256"])
    assert re.fullmatch(r"[0-9a-f-]{36}", MANIFEST["measurement"]["seed"])
    quality_order = ["Unknown", "Excellent", "VeryGood", "Good", "Fair", "Poor", "Experimental"]
    for generator in MANIFEST["generators"]:
        name = generator["name"]
        source = (ROOT / "Runtime" / "Core" / "Random" / f"{name}.cs").read_text(encoding="utf-8")
        attribution = re.search(r"\[RandomGeneratorMetadata\(\s*RandomQuality\.(\w+)", source)
        assert attribution is not None, name
        assert attribution[1] == generator["quality"], name
        quality_rank = quality_order.index(generator["quality"])
        assert sorted(generator["widths"]) == sorted(WIDTHS), name
        for width in WIDTHS:
            outcome = generator["widths"][width]
            assert outcome["reason"], (name, width)
            assert outcome["expected"] in ("pass", "fail"), (name, width)
            if outcome["expected"] == "fail":
                assert quality_rank > quality_order.index("Good"), (name, width)
                assert "cleanThrough" not in outcome, (name, width)
                assert "failsBy" in outcome, (name, width)
                if (limit := outcome["failsBy"]) is not None:
                    assert parse_length(limit) > 0
            else:
                assert "failsBy" not in outcome, (name, width)
                assert "cleanThrough" in outcome, (name, width)


@pytest.mark.parametrize("width", WIDTHS)
def test_recorded_clean_depth_discriminates_the_deepest_control(width: str) -> None:
    outcomes = [generator["widths"][width] for generator in MANIFEST["generators"]]
    deepest = max(
        parse_length(token)
        for outcome in outcomes
        if outcome["expected"] == "fail" and (token := outcome.get("failsBy")) is not None
    )
    assert deepest > 0
    for outcome in outcomes:
        if outcome["expected"] == "pass" and (token := outcome.get("cleanThrough")) is not None:
            assert parse_length(token) >= deepest


def test_length_units() -> None:
    assert parse_length("1GB") == 1 << 30
    assert parse_length("8GB") == 8 << 30
    assert parse_length("64KB") == 64 << 10
    with pytest.raises(ValueError, match="Unrecognized PractRand length"):
        parse_length("1 gigabyte")


@pytest.mark.parametrize("variant", [CLEAN, "\ufeff" + CLEAN, CLEAN.replace("\n", "\r\n")])
def test_clean_report(variant: str) -> None:
    result = classify_report(variant)
    assert result.observed == "pass"
    assert result.last_length == CLEAN_LENGTH


@pytest.mark.parametrize("evaluation", ["unusual", "normal", "normalish"])
def test_only_fail_in_evaluation_column_counts(evaluation: str) -> None:
    report = SUSPICIOUS.replace("unusual", evaluation).replace("DC6-9x1Bytes-1", "FAIL-name-only")
    assert classify_report(report).observed == "pass"


@pytest.mark.parametrize(
    "row",
    [
        FAIL_ROW,
        "  BRank(12):256(1)                  R>+99999  p~=  0   FAIL !!!!!!!!",
        "  BRank(12):256(1)                  R<-99999  p =  1   FAIL !!!!!!!!",
        '  BRank(12):256(1)                  R= +9999  "fail"   FAIL !',
        "  BRank(12):256(1)                  R= +9999            FAIL !",
        FAIL_ROW.replace("FAIL !!!!!!!", "fail"),
    ],
)
def test_failure_row_shapes(row: str) -> None:
    result = classify_report(FAILED.replace(FAIL_ROW, row))
    assert result.observed == "fail"
    assert len(result.failures) == 1
    assert result.failures[0]["length"] == "16 megabytes (2^24 bytes)"


@pytest.mark.parametrize(
    "report", [value for _, value in MALFORMED], ids=[name for name, _ in MALFORMED]
)
def test_malformed_report_always_fails(report: str) -> None:
    assert classify_report(report).observed == "invalid"
    for width in WIDTHS:
        valid = reports_for(width)
        for subject in (PCG, "Sfc64Random", XORSHIFT):
            valid[subject] = for_width(report, width)
        results = evaluate(MANIFEST, valid, "8GB", width)
        for subject in (PCG, "Sfc64Random", XORSHIFT):
            assert result_for(results, subject)["status"] == "error", (width, subject)


@pytest.mark.parametrize("width", WIDTHS)
def test_measurement_identity_depth_and_completeness(width: str) -> None:
    other_width = "64" if width == "32" else "32"
    malformed = (
        for_width(CLEAN, width),
        for_width(CLEAN.replace("version 0.95", "version 0.94"), width),
        for_width(CLEAN.replace(CLEAN_LENGTH, LONG_LENGTH), other_width),
    )
    for report in malformed:
        reports = reports_for(width)
        reports[PCG] = report
        assert result_for(evaluate(MANIFEST, reports, "8GB", width), PCG)["status"] == "error"
    reports = reports_for(width)
    del reports[PCG]
    missing = result_for(evaluate(MANIFEST, reports, "8GB", width), PCG)
    assert missing["observed"] == "missing"
    assert missing["status"] == "error"
    shallow = evaluate(MANIFEST, reports_for(width, clean_length=CLEAN_LENGTH), "1GB", width)
    assert result_for(shallow, PCG)["status"] == "inconclusive"
    assert result_for(shallow, XORSHIFT)["status"] == "ok"


@pytest.mark.parametrize("width", WIDTHS)
def test_manifest_accepts_only_qualified_results(width: str) -> None:
    matching = evaluate(MANIFEST, reports_for(width), "32GB", width)
    assert all(result["status"] in ("ok", "inconclusive") for result in matching)
    for generator in MANIFEST["generators"]:
        outcome = generator["widths"][width]
        name = generator["name"]
        if outcome["expected"] == "pass" and outcome.get("cleanThrough") is None:
            assert result_for(matching, name)["status"] == "inconclusive"
            measured = evaluate(MANIFEST, reports_for(width, {name: "fail"}), "8GB", width)
            assert "No passing baseline" in result_for(measured, name)["detail"]
            assert result_for(measured, name)["status"] == "error"
        if outcome["expected"] == "fail" and outcome.get("failsBy") is None:
            measured = evaluate(MANIFEST, reports_for(width, {name: "pass"}), "32GB", width)
            assert result_for(measured, name)["status"] == "inconclusive"
    regression = evaluate(MANIFEST, reports_for(width, {PCG: "fail"}), "8GB", width)
    assert result_for(regression, PCG)["status"] == "error"
    broken = evaluate(MANIFEST, reports_for(width, {XORSHIFT: "pass"}), "1GB", width)
    assert result_for(broken, XORSHIFT)["status"] == "error"
    assert "harness is broken" in result_for(broken, XORSHIFT)["detail"]
    changed = copy.deepcopy(MANIFEST)
    subject = next(generator for generator in changed["generators"] if generator["name"] == PCG)
    subject["widths"][width]["cleanThrough"] = None
    assert (
        result_for(evaluate(changed, reports_for(width), "32GB", width), PCG)["status"]
        == "inconclusive"
    )
    subject["widths"][width]["cleanThrough"] = "8GB"
    assert result_for(evaluate(changed, reports_for(width), "32GB", width), PCG)["status"] == "ok"


def test_missing_clean_depth_does_not_pass() -> None:
    changed = copy.deepcopy(MANIFEST)
    subject = next(generator for generator in changed["generators"] if generator["name"] == PCG)
    del subject["widths"]["32"]["cleanThrough"]
    with pytest.raises(ValueError, match="no recorded cleanThrough setting"):
        evaluate(changed, reports_for("32"), "32GB", "32")


def test_control_discrimination_uses_actual_width_and_depth() -> None:
    for width, depth, status in (
        ("32", CLEAN_LENGTH, "inconclusive"),
        ("32", "8 gigabytes (2^33 bytes)", "error"),
        ("64", LONG_LENGTH, "inconclusive"),
    ):
        results = evaluate(MANIFEST, reports_for(width, {SYSTEM: "pass"}, depth), "1GB", width)
        assert result_for(results, SYSTEM)["status"] == status
    with pytest.raises(ValueError, match="no recorded outcome for width 128"):
        evaluate(MANIFEST, reports_for("32"), "8GB", "128")


def write_reports(directory: Path, reports: dict[str, str]) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    for name, report in reports.items():
        (directory / f"{name}.txt").write_text(report, encoding="utf-8")


@pytest.mark.parametrize("width", WIDTHS)
def test_cli_for_full_inventory_and_selection(tmp_path: Path, width: str) -> None:
    directory = tmp_path / "reports with spaces"
    output = tmp_path / "summary.json"
    markdown = tmp_path / "summary.md"

    def invoke(selection: object = None) -> subprocess.CompletedProcess[str]:
        command = [
            sys.executable,
            str(SCRIPTS / "evaluate_practrand.py"),
            "--reports",
            str(directory),
            "--budget",
            "32GB",
            "--width",
            width,
            "--seed",
            MANIFEST["measurement"]["seed"],
            "--out-json",
            str(output),
            "--out-md",
            str(markdown),
        ]
        if selection is not None:
            command.extend(["--generators", json.dumps(selection)])
        return subprocess.run(
            command, cwd=ROOT, capture_output=True, text=True, check=False, timeout=30
        )

    full_reports = reports_for(width)
    write_reports(directory, full_reports)
    full = invoke()
    assert full.returncode == 0
    assert full.stdout.endswith("mismatch-count=0\n")
    assert full.stdout == markdown.read_text(encoding="utf-8") + "mismatch-count=0\n"
    assert f"{width}-bit" in full.stdout
    assert all(name in full.stdout for name in NAMES)
    summary = cast("dict[str, object]", json.loads(output.read_text(encoding="utf-8")))
    assert summary["scope"] == "full"
    assert summary["recoverable"] is False
    write_reports(directory, {PCG: "", "Sfc64Random": ""})
    malformed = invoke(NAMES)
    assert malformed.returncode == 1
    assert "mismatch-count=2" in malformed.stdout
    assert "Invalid or incomplete PractRand report" in malformed.stdout
    for path in directory.glob("*.txt"):
        path.unlink()
    write_reports(directory, {XORSHIFT: full_reports[XORSHIFT]})
    focused = invoke([XORSHIFT])
    assert focused.returncode == 0
    assert "Scope: **selected**" in focused.stdout
    summary = cast("dict[str, object]", json.loads(output.read_text(encoding="utf-8")))
    assert summary["scope"] == "selected"
    assert summary["recoverable"] is False
    rows = cast("list[Result]", summary["results"])
    assert [row["name"] for row in rows] == [XORSHIFT]
    assert invoke().returncode == 1
    (directory / f"{XORSHIFT}.txt").unlink()
    assert invoke([XORSHIFT]).returncode == 1
    write_reports(directory, {XORSHIFT: ""})
    assert invoke([XORSHIFT]).returncode == 1
    write_reports(directory, {XORSHIFT: full_reports[XORSHIFT], PCG: full_reports[PCG]})
    for selection in ([], [XORSHIFT, XORSHIFT], ["UnknownRandom"], XORSHIFT, [None]):
        result = invoke(selection)
        assert result.returncode == 1
        assert "--generators must be a nonempty JSON array" in result.stderr
    unlisted = invoke([XORSHIFT])
    assert unlisted.returncode == 1
    assert "absent from the selected inventory" in unlisted.stdout


WorkflowStep = dict[str, object]


@pytest.mark.parametrize(
    "scenario", ["selected", "full-inconclusive", "full-conclusive", "missing-selected"]
)
@pytest.mark.skipif(os.name == "nt", reason="The hosted workflow owns a POSIX Bash step.")
def test_hosted_compare_and_incident_transition(tmp_path: Path, scenario: str) -> None:
    workflow_path = ROOT / ".github" / "workflows" / "random-quality.yml"
    workflow = cast(
        "dict[str, dict[str, dict[str, list[WorkflowStep]]]]",
        yaml.safe_load(workflow_path.read_text(encoding="utf-8")),
    )
    steps = workflow["jobs"]["evaluate"]["steps"]
    compare = next(step for step in steps if step.get("id") == "compare")
    issue = next(step for step in steps if step.get("name") == "Open or update the mismatch issue")
    compare_env = cast("dict[str, str]", compare["env"])
    issue_env = cast("dict[str, str]", issue["env"])
    assert compare_env["GENERATORS"] == "${{ needs.resolve.outputs.generators }}"
    assert issue_env["RECOVERABLE"] == "${{ steps.compare.outputs.recoverable }}"
    bash = shutil.which("bash")
    assert bash is not None

    scripts = tmp_path / "scripts"
    sandbox = scripts / "random-quality"
    sandbox.mkdir(parents=True)
    shutil.copyfile(SCRIPTS / "evaluate_practrand.py", sandbox / "evaluate_practrand.py")
    project = scripts / "python~"
    project.mkdir()
    for filename in ("pyproject.toml", "uv.lock"):
        shutil.copyfile(ROOT / "scripts" / "python~" / filename, project / filename)
    fixture: Manifest = copy.deepcopy(MANIFEST)
    if scenario == "full-conclusive":
        fixture["generators"] = [
            generator for generator in fixture["generators"] if generator["name"] == XORSHIFT
        ]
    manifest_path = tmp_path / "manifest.json"
    manifest_path.write_text(json.dumps(fixture), encoding="utf-8")
    selected = (
        [generator["name"] for generator in fixture["generators"]]
        if scenario.startswith("full")
        else [XORSHIFT]
    )
    for width in WIDTHS:
        reports = reports_for(width)
        destination = tmp_path / ".artifacts" / "random-quality" / "reports" / width
        write_reports(
            destination,
            {
                name: reports[name]
                for name in selected
                if scenario != "missing-selected" or width != "64"
            },
        )
    outputs = tmp_path / "outputs"
    environment = {
        **os.environ,
        "MANIFEST": str(manifest_path),
        "GENERATORS": json.dumps(selected),
        "BUDGET": "32GB",
        "SEED": MANIFEST["measurement"]["seed"],
        "RUN_URL": "local-control",
        "GITHUB_STEP_SUMMARY": str(tmp_path / "summary"),
        "GITHUB_OUTPUT": str(outputs),
        "UV_PYTHON": sys.executable,
    }
    comparison = subprocess.run(
        [bash, "-c", cast("str", compare["run"])],
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert comparison.returncode == 0, comparison.stderr
    assert outputs.read_text(encoding="utf-8").splitlines() == [
        f"mismatched={str(scenario == 'missing-selected').lower()}",
        f"recoverable={str(scenario == 'full-conclusive').lower()}",
    ]
    calls = tmp_path / "issue-calls"
    issue_script = cast("str", issue["run"])
    stub = """gh() {
        printf '%s\\n' "$*" >> "$ISSUE_CALLS"
        if [ "$1 $2" = "issue list" ]; then printf '713\\n'; fi
    }
    """
    notification = subprocess.run(
        [bash, "-c", stub + issue_script],
        cwd=tmp_path,
        env={
            **environment,
            "ISSUE_CALLS": str(calls),
            "ISSUE_TITLE": "control",
            "PRACTRAND_VERSION": "0.95",
            "MISMATCHED": str(scenario == "missing-selected").lower(),
            "RECOVERABLE": str(scenario == "full-conclusive").lower(),
        },
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert notification.returncode == 0, notification.stderr
    commands = calls.read_text(encoding="utf-8")
    assert ("issue close" in commands) is (scenario == "full-conclusive")
    assert ("issue comment" in commands) is (scenario in {"full-conclusive", "missing-selected"})
