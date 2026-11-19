# MIT License - Copyright (c) 2026 wallstop
# Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

"""Compare completed PractRand reports with the recorded per-width outcomes."""

from __future__ import annotations

import json
import math
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Literal, NotRequired, TypedDict, cast

UNITS = {"KB": 2**10, "MB": 2**20, "GB": 2**30, "TB": 2**40}
DISPLAY_UNITS = {"kilobyte": "KB", "megabyte": "MB", "gigabyte": "GB", "terabyte": "TB"}
HEADER = re.compile(
    r"RNG_test using PractRand version (\S+)\n"
    r"RNG = RNG_stdin(32|64), seed = unknown\n"
    r"test set = core, folding = standard \(\2 bit\)\n\n"
)
LENGTH = re.compile(r"length=\s*(.+?\(2\^([0-9]+) bytes\)),\s*time=\s*[0-9]+(?:\.[0-9]+)? seconds")
DISPLAY_LENGTH = re.compile(
    r"([0-9]+(?:\.[0-9]+)?) (kilobyte|megabyte|gigabyte|terabyte)s? \(2\^[0-9]+ bytes\)"
)
ROW = re.compile(
    r"""(\S.*?)\s*R[=<>]\s*([-+]?[0-9]+(?:\.[0-9]+)?(?:e[-+]?[0-9]+)?)\s+"""
    r"""(?:(?:p(?:~=|\s*=)\s*((?:1-)?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:e[-+]?[0-9]+)?)|"(?:pass|fail)")\s+)?"""
    r"""(normal(?:ish)?|unusual|(?:mildly |very )?suspicious|FAIL[ !]*)""",
    re.I,
)


class Battery(TypedDict):
    name: str
    version: str
    url: str
    sha256: str


class Measurement(TypedDict):
    seed: str


class Outcome(TypedDict):
    expected: Literal["pass", "fail"]
    reason: str
    failsBy: NotRequired[str | None]
    cleanThrough: NotRequired[str | None]


class Generator(TypedDict):
    name: str
    quality: str
    widths: dict[str, Outcome]


class Manifest(TypedDict):
    battery: Battery
    measurement: Measurement
    generators: list[Generator]


class Failure(TypedDict):
    test: str
    length: str
    line: str


class Result(TypedDict):
    name: str
    width: NotRequired[str]
    quality: NotRequired[str]
    expected: str
    observed: str
    reachedLength: NotRequired[str]
    failures: NotRequired[list[Failure]]
    status: Literal["ok", "error", "inconclusive"]
    detail: str


@dataclass(frozen=True)
class Report:
    observed: Literal["pass", "fail", "invalid"]
    failures: list[Failure]
    last_length: str
    reached_bytes: int
    detail: str = ""
    version: str = ""
    width: str = ""


@dataclass(frozen=True)
class Context:
    seed: str
    budget: str
    command: str
    run_url: str
    width: str
    scope: str


def parse_length(token: str) -> float:
    match = re.fullmatch(r"([0-9]+)(KB|MB|GB|TB)", token.strip())
    if match is None:
        raise ValueError(f"Unrecognized PractRand length '{token}'. Expected e.g. 1GB, 16MB, 64KB.")
    return float(match[1]) * UNITS[match[2]]


def read_rows(body: list[str], length: str, failures: list[Failure]) -> str:
    if len(body) == 1 and re.fullmatch(r"no anomalies in [1-9][0-9]* test result\(s\)", body[0]):
        return ""
    if not body or re.fullmatch(r"Test Name\s+Raw\s+Processed\s+Evaluation", body[0]) is None:
        return "A result block contains neither test counts nor an evaluated result table."
    evaluated = 0
    for index, line in enumerate(body[1:], start=1):
        if re.fullmatch(r"\.\.\.and [0-9]+ test result\(s\) without anomalies", line):
            if index != len(body) - 1 or evaluated == 0:
                return "An anomaly summary does not follow a complete result table."
            continue
        result = ROW.fullmatch(line)
        if result is None:
            return "A result table contains an incomplete or unrecognized evaluation row."
        raw = float(result[2])
        probability = result[3]
        if not math.isfinite(raw) or (
            probability is not None and not 0 <= float(probability.removeprefix("1-")) <= 1
        ):
            return "A result row contains a non-finite raw value or an out-of-range probability."
        evaluated += 1
        if re.fullmatch(r"FAIL[ !]*", result[4], re.I):
            failures.append({"test": result[1], "length": length, "line": line})
    return "" if evaluated else "The result table contains no evaluated tests."


def classify_report(text: object) -> Report:
    failures: list[Failure] = []
    last_length = ""
    reached_bytes = 0

    def invalid(detail: str) -> Report:
        return Report("invalid", failures, last_length, reached_bytes, detail)

    if not isinstance(text, str):
        return invalid("The report is not text.")
    normalized = text.removeprefix("\ufeff").replace("\r\n", "\n")
    header = HEADER.match(normalized)
    if header is None:
        return invalid("Missing or malformed PractRand version, stream or test-set header.")
    version, width = header.groups()
    for block in re.split(r"(?=^rng=)", normalized[header.end() :], flags=re.M):
        if not block:
            continue
        if re.search(r"\n[ \t]*\n\Z", block) is None:
            return invalid("The final result block is unterminated.")
        lines = block.rstrip().split("\n")
        if lines[0] != f"rng=RNG_stdin{width}, seed=unknown":
            return invalid("A result block has a missing or inconsistent stream header.")
        length = LENGTH.fullmatch(lines[1]) if len(lines) > 1 else None
        if length is None or not 0 <= float(length[2]) <= 52:
            return invalid("A result block has no valid measured byte length.")
        measured_bytes = 1 << int(float(length[2]))
        display = DISPLAY_LENGTH.fullmatch(length[1])
        if (
            display is None
            or float(display[1]) * UNITS[DISPLAY_UNITS[display[2]]] != measured_bytes
        ):
            return invalid("The displayed length does not agree with its measured byte exponent.")
        if measured_bytes <= reached_bytes:
            return invalid("Measured byte lengths must increase between result blocks.")
        display_length = length[1].strip()
        error = read_rows([line.strip() for line in lines[2:]], display_length, failures)
        if error:
            return invalid(error)
        reached_bytes = measured_bytes
        last_length = display_length
    if not reached_bytes:
        return invalid("The report contains no completed result blocks.")
    return Report(
        "fail" if failures else "pass",
        failures,
        last_length,
        reached_bytes,
        version=version,
        width=width,
    )


def outcome_for(generator: Generator, width: str) -> Outcome:
    outcome = (generator.get("widths") or {}).get(width)
    if outcome is None:
        raise ValueError(
            f"{generator['name']} has no recorded outcome for width {width}. "
            "The manifest must carry every width the workflow runs."
        )
    return outcome


def judge(
    classified: Report, outcome: Outcome, battery: Battery, name: str, width: str
) -> tuple[Literal["ok", "error", "inconclusive"], str]:
    observed = classified.observed
    expected = outcome["expected"]
    length = classified.last_length
    clean_through = outcome.get("cleanThrough")
    if expected == "pass" and observed == "pass":
        if "cleanThrough" not in outcome:
            raise ValueError(f"{name} has no recorded cleanThrough setting for width {width}.")
        if clean_through is None:
            return (
                "inconclusive",
                (
                    f"{name} has no recorded clean length on the {width}-bit stream. "
                    "This report is an initial measurement, not agreement with an established baseline. "
                    "Review the report before recording cleanThrough; see the manifest reason."
                ),
            )
        if classified.reached_bytes < parse_length(clean_through):
            return (
                "inconclusive",
                (
                    f"{name} was measured only through {length}, below its recorded {clean_through} baseline. "
                    "This run cannot confirm that baseline at its recorded depth."
                ),
            )
    if observed == expected:
        return "ok", "Outcome matches the manifest."
    if expected == "pass":
        message = (
            "No passing baseline was recorded; review this failure and the generator's quality rating."
            if "cleanThrough" in outcome and clean_through is None
            else "This is a statistical regression in the generator."
        )
        return (
            "error",
            (
                f"{name} is expected to pass {battery['name']} {battery['version']} on the {width}-bit "
                f"stream but failed at {length or 'an unrecorded length'}. {message}"
            ),
        )
    fails_by = outcome.get("failsBy")
    if not fails_by:
        return (
            "inconclusive",
            (
                f"{name} has no measured failing length, so this battery cannot discriminate it at any "
                "budget. Recorded as inconclusive by design; see the manifest reason."
            ),
        )
    if classified.reached_bytes >= parse_length(fails_by):
        return (
            "error",
            (
                f"{name} is a deliberate expected-failure control that must fail by {fails_by} "
                f"on the {width}-bit stream, but it PASSED through {length}. A weak generator passing "
                "is evidence the harness is broken, not that the generator improved."
            ),
        )
    return (
        "inconclusive",
        (
            f"{name} only fails at {fails_by}; the measured {length} is too short to "
            "discriminate it. Raise the byte budget to assert this control."
        ),
    )


def evaluate(manifest: Manifest, reports: dict[str, str], budget: str, width: str) -> list[Result]:
    budget_bytes = parse_length(budget)
    battery = manifest["battery"]
    results: list[Result] = []
    for generator in manifest["generators"]:
        name = generator["name"]
        outcome = outcome_for(generator, width)
        expected = outcome["expected"]
        if name not in reports:
            results.append(
                {
                    "name": name,
                    "width": width,
                    "expected": expected,
                    "observed": "missing",
                    "status": "error",
                    "detail": "No PractRand report was produced for this generator.",
                }
            )
            continue
        classified = classify_report(reports[name])
        observed = classified.observed
        last_length = classified.last_length
        error = classified.detail
        if not error and (classified.version != battery["version"] or classified.width != width):
            error = "The report version or stream width does not match this campaign."
        if not error and observed == "pass" and classified.reached_bytes < budget_bytes:
            error = f"The clean report reached only {last_length}, short of the requested {budget}."
        if error:
            results.append(
                {
                    "name": name,
                    "width": width,
                    "expected": expected,
                    "observed": observed,
                    "reachedLength": last_length,
                    "status": "error",
                    "detail": f"Invalid or incomplete PractRand report: {error}",
                }
            )
            continue
        status, detail = judge(classified, outcome, battery, name, width)
        results.append(
            {
                "name": name,
                "width": width,
                "quality": generator["quality"],
                "expected": expected,
                "observed": observed,
                "reachedLength": last_length,
                "failures": classified.failures[:5],
                "status": status,
                "detail": detail,
            }
        )
    return results


def render_markdown(battery: Battery, results: list[Result], context: Context) -> str:
    icons = {"ok": "OK", "error": "MISMATCH", "inconclusive": "INCONCLUSIVE"}
    lines = [
        f"# Random quality battery report ({context.width}-bit stream)",
        "",
        f"- Battery: **{battery['name']} {battery['version']}** (`{battery['sha256']}`)",
        f"- Source: {battery['url']}",
        f"- Seed: `{context.seed}`",
        f"- Stream width: **{context.width}-bit**",
        f"- Scope: **{context.scope}** ({len(results)} generators)",
        f"- Byte budget per generator: **{context.budget}**",
        f"- Command: `{context.command}`",
        f"- Run: {context.run_url or '(local)'}",
        "",
        "| Generator | Quality | Expected | Observed | Reached | Result |",
        "| --- | --- | --- | --- | --- | --- |",
    ]
    lines.extend(
        f"| {result['name']} | {result.get('quality') or '-'} | {result['expected']} | "
        f"{result['observed']} | {result.get('reachedLength') or '-'} | {icons[result['status']]} |"
        for result in results
    )
    problems = [result for result in results if result["status"] == "error"]
    if problems:
        lines.extend(["", "## Mismatches", ""])
        for problem in problems:
            lines.extend([f"### {problem['name']}", "", problem["detail"], ""])
            lines.extend(
                f"- `{failure['line']}` at {failure['length']}"
                for failure in problem.get("failures", [])
            )
            lines.append("")
    inconclusive = [result for result in results if result["status"] == "inconclusive"]
    if inconclusive:
        lines.extend(["", "## Inconclusive outcomes", ""])
        lines.extend(f"- **{result['name']}**: {result['detail']}" for result in inconclusive)
    return "\n".join(lines) + "\n"


def read_arg(arguments: list[str], name: str, fallback: str | None = None) -> str:
    if name in arguments:
        index = arguments.index(name)
        if index + 1 < len(arguments):
            return arguments[index + 1]
    if fallback is None:
        raise ValueError(f"Missing required argument {name}.")
    return fallback


def read_manifest(path: Path) -> Manifest:
    return cast("Manifest", json.loads(path.read_text(encoding="utf-8")))


def selected_generators(arguments: list[str], manifest: Manifest) -> list[str]:
    names = [generator["name"] for generator in manifest["generators"]]
    selected: object = (
        json.loads(read_arg(arguments, "--generators")) if "--generators" in arguments else names
    )
    if isinstance(selected, list):
        items = cast("list[object]", selected)
        valid = [item for item in items if isinstance(item, str) and item in names]
        if valid and len(valid) == len(items) == len(set(valid)):
            return valid
    raise ValueError(
        "--generators must be a nonempty JSON array of unique manifest generator names."
    )


def main(arguments: list[str]) -> int:
    manifest_path = read_arg(
        arguments, "--manifest", "scripts/random-quality/expected-outcomes.json"
    )
    reports_directory = read_arg(arguments, "--reports")
    reports_path = Path(reports_directory)
    budget = read_arg(arguments, "--budget")
    seed = read_arg(arguments, "--seed")
    command = read_arg(arguments, "--command", "RNG_test stdin32")
    width = read_arg(arguments, "--width", "32")
    run_url = read_arg(arguments, "--run-url", "")
    out_markdown = read_arg(arguments, "--out-md", "")
    out_json = read_arg(arguments, "--out-json", "")
    manifest = read_manifest(Path(manifest_path))
    selected = selected_generators(arguments, manifest)
    scope = "full" if len(selected) == len(manifest["generators"]) else "selected"
    manifest["generators"] = [
        generator for generator in manifest["generators"] if generator["name"] in selected
    ]
    reports = (
        {
            path.name.removesuffix(".txt"): path.read_bytes().decode("utf-8", errors="replace")
            for path in sorted(
                reports_path.iterdir(),
                key=lambda path: path.name.encode("utf-16-be", errors="surrogatepass"),
            )
            if path.name.endswith(".txt")
        }
        if reports_directory and reports_path.exists()
        else {}
    )
    results = evaluate(manifest, reports, budget, width)
    for name, text in reports.items():
        if name not in selected:
            results.append(
                {
                    "name": name,
                    "expected": "(unlisted)",
                    "observed": classify_report(text).observed,
                    "status": "error",
                    "failures": [],
                    "detail": f"{name} produced a report but is absent from the selected inventory in {manifest_path}.",
                }
            )
    recoverable = (
        scope == "full"
        and seed == manifest["measurement"]["seed"]
        and all(result["status"] == "ok" for result in results)
    )
    battery = manifest["battery"]
    markdown = render_markdown(
        battery, results, Context(seed, budget, command, run_url, width, scope)
    )
    if out_markdown:
        Path(out_markdown).write_text(markdown, encoding="utf-8", newline="\n")
    if out_json:
        document = {
            "manifest": battery,
            "seed": seed,
            "budget": budget,
            "width": width,
            "scope": scope,
            "recoverable": recoverable,
            "results": results,
        }
        Path(out_json).write_text(
            json.dumps(document, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
    sys.stdout.write(markdown)
    mismatches = sum(result["status"] == "error" for result in results)
    print(f"mismatch-count={mismatches}")
    return int(mismatches > 0)


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(error, file=sys.stderr)
        sys.exit(1)
