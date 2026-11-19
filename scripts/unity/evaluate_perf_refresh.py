"""Decide whether a downloaded Unity benchmark matrix can replace rolling results."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TypedDict, cast

EXTRACTOR = Path(__file__).with_name("lib") / "extract-perf-metrics.js"
RESULT_FILE = re.compile(r"results-[^\r\n\u2028\u2029]*\.xml", re.IGNORECASE | re.ASCII)
REQUIRED_OPTIONS = (
    "--benchmark-result",
    "--allow-baseline-refresh",
    "--expected-files",
    "--candidate-dir",
    "--output",
)
MISSING_OPTIONS = f"{', '.join(REQUIRED_OPTIONS[:-1])}, and --output are required."
COUNT_METRICS = """
const fs = require("node:fs");
const { extractFromFiles } = require(process.argv[1]);
try {
  const paths = JSON.parse(fs.readFileSync(0, "utf8"));
  const counts = paths.map((path) =>
    extractFromFiles([path], { unityVersion: null, testMode: null }).length);
  process.stdout.write(JSON.stringify(counts));
} catch (error) {
  process.stderr.write(error.message + "\\n");
  process.exitCode = 1;
}
"""


class Decision(TypedDict):
    complete: bool
    invalidSuccessfulMatrix: bool
    reasons: list[str]
    expectedFiles: list[str]
    actualFiles: list[str]
    expectedCount: int
    actualCount: int
    filesWithMetrics: int
    missingFiles: list[str]
    unexpectedFiles: list[str]
    zeroMetricFiles: list[str]


def evaluate_refresh(
    benchmark_result: str,
    allow_baseline_refresh: bool,
    expected_files: list[str],
    actual_files: list[str],
    metric_counts: dict[str, int],
) -> Decision:
    expected = sorted(expected_files, key=utf16_order)
    actual = sorted(actual_files, key=utf16_order)
    expected_names = set(expected)
    actual_names = set(actual)
    matching_identities = expected == actual
    populated = sum(metric_counts.get(file, 0) > 0 for file in expected)
    metrics_complete = populated == len(expected)
    missing = [file for file in expected if file not in actual_names]
    unexpected = [file for file in actual if file not in expected_names]
    empty = [file for file in expected if file in actual_names and not metric_counts.get(file, 0)]

    reasons: list[str] = []
    if not allow_baseline_refresh:
        reasons.append(
            "this pinned dispatch is diagnostic-only and cannot replace the canonical baseline"
        )
    if benchmark_result != "success":
        reasons.append(f"benchmark aggregate result is {benchmark_result}")
    if not expected:
        reasons.append("the expected result matrix is empty")
    if not matching_identities:
        reasons.append(
            f"result identities differ (missing: {', '.join(missing) or 'none'}; "
            f"unexpected: {', '.join(unexpected) or 'none'})"
        )
    if not metrics_complete:
        reasons.append(f"results with no metrics: {', '.join(empty) or 'none'}")

    return {
        "complete": not reasons,
        "invalidSuccessfulMatrix": (
            allow_baseline_refresh
            and benchmark_result == "success"
            and (not expected or not matching_identities or not metrics_complete)
        ),
        "reasons": reasons,
        "expectedFiles": expected,
        "actualFiles": actual,
        "expectedCount": len(expected),
        "actualCount": len(actual),
        "filesWithMetrics": populated,
        "missingFiles": missing,
        "unexpectedFiles": unexpected,
        "zeroMetricFiles": empty,
    }


def utf16_order(name: str) -> bytes:
    # Keep the previous JavaScript sort order in decision files.
    return name.encode("utf-16-be", "surrogatepass")


def inventory(directory: str) -> list[str]:
    if not Path(directory).exists():
        return []
    with os.scandir(directory) as entries:
        return [
            entry.name
            for entry in entries
            if entry.is_file(follow_symlinks=False) and RESULT_FILE.fullmatch(entry.name)
        ]


def count_metrics(directory: str, actual_files: list[str]) -> dict[str, int]:
    if not actual_files:
        return {}
    node = shutil.which("node")
    if node is None:
        raise ValueError("Node is required to count benchmark metrics.")
    paths = [str(Path(directory) / file) for file in actual_files]
    # The publisher consumes the same extractor. Count its metrics per file
    # in one process so validation and publication interpret XML identically.
    result = subprocess.run(  # noqa: S603 -- fixed program, input paths arrive as JSON on stdin
        [str(Path(node).resolve()), "-e", COUNT_METRICS, str(EXTRACTOR)],
        input=json.dumps(paths),
        capture_output=True,
        encoding="utf-8",
        check=False,
    )
    if result.returncode:
        raise ValueError(
            result.stderr.rstrip("\r\n") or f"metric extractor exited {result.returncode}"
        )
    payload: object = json.loads(result.stdout)
    if not isinstance(payload, list):
        raise ValueError("invalid metric counts from extractor")
    counts = cast("list[object]", payload)
    if len(counts) != len(actual_files):
        raise ValueError("metric extractor must return one count per result file")
    counts_by_file: dict[str, int] = {}
    for file, count in zip(actual_files, counts, strict=True):
        if type(count) is not int or count < 0:
            raise ValueError("metric extractor returned an invalid count")
        counts_by_file[file] = count
    return counts_by_file


def parse_options(arguments: list[str]) -> tuple[dict[str, str], bool]:
    options: dict[str, str] = {}
    self_test = False
    arguments_iterator = iter(arguments)
    for option in arguments_iterator:
        if option == "--self-test":
            self_test = True
            continue
        value = next(arguments_iterator, None)
        if value is None:
            raise ValueError(f"{option} requires a value.")
        if option not in REQUIRED_OPTIONS:
            raise ValueError(f"Unknown argument: {option}")
        options[option] = value
    return options, self_test


def run_self_test() -> None:
    files = ["results-2022-editmode.xml", "results-2022-playmode.xml"]
    counts = dict.fromkeys(files, 1)
    cases = (
        ("success", True, files[::-1], counts, True, False),
        ("failure", True, files, counts, False, False),
        ("success", True, files[:1], counts, False, True),
        (
            "success",
            True,
            [*files, "results-unexpected-editmode.xml"],
            counts,
            False,
            True,
        ),
        ("success", True, files, {files[0]: 1}, False, True),
        ("success", False, files, counts, False, False),
    )
    for status, allowed, actual, metrics, complete, invalid in cases:
        decision = evaluate_refresh(status, allowed, files, actual, metrics)
        if (decision["complete"], decision["invalidSuccessfulMatrix"]) != (
            complete,
            invalid,
        ):
            raise ValueError(f"Self-test failed: {decision}")
    print("evaluate-perf-refresh self-test passed (6 matrix-policy cases).")


def main(arguments: list[str]) -> int:
    try:
        options, self_test = parse_options(arguments)
        if self_test:
            run_self_test()
            return 0
        if any(option not in options for option in REQUIRED_OPTIONS):
            raise ValueError(MISSING_OPTIONS)
        expected_payload: object = json.loads(options["--expected-files"])
        if not (
            isinstance(expected_payload, list)
            and options["--benchmark-result"]
            and options["--candidate-dir"]
            and options["--output"]
        ):
            raise ValueError(MISSING_OPTIONS)
        expected_entries = cast("list[object]", expected_payload)
        expected_files: list[str] = []
        for entry in expected_entries:
            if not isinstance(entry, str):
                raise ValueError("--expected-files must contain only result filenames.")
            expected_files.append(entry)
        candidate_dir = options["--candidate-dir"]
        files = inventory(candidate_dir)
        decision = evaluate_refresh(
            options["--benchmark-result"],
            options["--allow-baseline-refresh"] == "true",
            expected_files,
            files,
            count_metrics(candidate_dir, files),
        )
        Path(options["--output"]).write_bytes(
            (json.dumps(decision, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        )
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
