"""Evaluate TestU01 report completeness and summarize measured generators."""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass
from io import TextIOWrapper
from pathlib import Path
from typing import NoReturn, cast

DECISIVE = 1e-10
MAX_SAFE_INTEGER = (1 << 53) - 1
SENTINELS = {"eps": 0.0, "eps1": 1e-16}
HEADER = re.compile(r"^=+ Summary results of (?:SmallCrush|Crush|BigCrush) =+\s*$", re.M)
COUNT = re.compile(r"^\s*Number of statistics:\s*([0-9]+)\s*$", re.M)
LISTED = re.compile(r"The following tests gave p-values\s+outside \[0\.001,\s*0\.9990\]:")
ROW = re.compile(r"([0-9]+)\s{2,}(\S.*?)\s{2,}(\S.*?)\s*")
DASHES = re.compile(r"-+")
PROBABILITY = re.compile(r"(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:e[+-]?[0-9]+)?", re.I)


@dataclass(frozen=True)
class Reading:
    test: str
    raw: str
    extremity: float


@dataclass(frozen=True)
class Verdict:
    ran_battery: bool
    decisive: tuple[Reading, ...] = ()
    marginal: tuple[Reading, ...] = ()

    @property
    def failed(self) -> bool:
        return bool(self.decisive)


def extremity(raw: str) -> float | None:
    text = raw.strip()
    complement = re.fullmatch(r"1\s*-\s*(\S+)", text)
    body = complement[1] if complement is not None else text
    if body in SENTINELS:
        return SENTINELS[body]
    if PROBABILITY.fullmatch(body) is None:
        return None
    value = float(body)
    if not 0 <= value <= 1:
        return None
    return min(value, 1 - value)


def verdict(text: str) -> Verdict:
    invalid = Verdict(False)
    headers = list(HEADER.finditer(text))
    if len(headers) != 1 or "input stream exhausted" in text:
        return invalid
    summary = text[headers[0].start() :]
    lines = [line for raw in summary.split("\n") if (line := raw.strip())]
    counts = COUNT.findall(summary)
    if len(counts) != 1:
        return invalid
    digits = counts[0].lstrip("0")
    if not digits or len(digits) > 16 or int(digits) > MAX_SAFE_INTEGER:
        return invalid
    count = int(digits)
    listed = LISTED.search(summary) is not None
    if lines[-1] == "All tests were passed":
        return Verdict(not listed and not any(re.match(r"[0-9]+\s{2,}", line) for line in lines))
    if not listed:
        return invalid
    footer = lines[-1] == "All other tests were passed"
    table_end = len(lines) - (2 if footer else 1)
    table = next(
        (index for index, line in enumerate(lines) if re.fullmatch(r"Test\s+p-value", line)), -1
    )
    if table < 0 or table + 2 >= table_end:
        return invalid
    if DASHES.fullmatch(lines[table + 1]) is None or DASHES.fullmatch(lines[table_end]) is None:
        return invalid
    rows = lines[table + 2 : table_end]
    # TestU01 omits the footer when all but at most one statistic appears in the table.
    if len(rows) > count or (not footer and len(rows) < count - 1):
        return invalid
    decisive: list[Reading] = []
    marginal: list[Reading] = []
    for line in rows:
        match = ROW.fullmatch(line)
        if match is None or not match[1].lstrip("0"):
            return invalid
        distance = extremity(match[3])
        if distance is None:
            return invalid
        reading = Reading(match[2], match[3], distance)
        (decisive if distance < DECISIVE else marginal).append(reading)
    return Verdict(True, tuple(decisive), tuple(marginal))


def describe(generator: str, rows: tuple[Reading, ...]) -> str:
    return f"{generator}: " + ", ".join(f"{row.test}={row.raw}" for row in rows)


def read_text(path: Path) -> str:
    with path.open(encoding="utf-8", errors="replace", newline="") as stream:
        return stream.read()


def reject_json_constant(value: str) -> NoReturn:
    raise ValueError(f"Invalid JSON constant: {value}.")


def read_expectations(manifest_path: Path) -> dict[str, str | None]:
    content = cast(
        "dict[str, object]",
        json.loads(read_text(manifest_path), parse_constant=reject_json_constant),
    )
    entries = cast("list[dict[str, object]] | None", content.get("generators")) or []
    expectations: dict[str, str | None] = {}
    for entry in entries:
        name = cast("str", entry.get("name"))
        widths = cast("dict[str, dict[str, object] | None] | None", entry.get("widths")) or {}
        width = widths.get("32") or {}
        if "expected" in width:
            expectations[name] = cast("str | None", width["expected"])
        else:
            expectations.pop(name, None)
    return expectations


def report(directory: str, manifest: str) -> int:
    if not directory or not Path(directory).exists():
        print(f"[testu01] No report directory at '{directory or 'undefined'}'.", file=sys.stderr)
        return 1
    manifest_path = Path(manifest)
    if not manifest_path.exists():
        print(
            f"[testu01] No manifest at '{manifest}'. Refusing to judge without expectations.",
            file=sys.stderr,
        )
        return 1
    expectations = read_expectations(manifest_path)
    reports = sorted(
        (path for path in Path(directory).iterdir() if path.name.endswith(".txt")),
        key=lambda path: path.name.encode("utf-16-be", errors="surrogatepass"),
    )
    if not reports:
        print(
            f"[testu01] '{directory}' holds no reports. A scan that looked at nothing is not a pass.",
            file=sys.stderr,
        )
        return 1
    broken: list[str] = []
    failing: list[str] = []
    confirmed: list[str] = []
    inconclusive: list[str] = []
    marginal: list[str] = []
    unknown: list[str] = []

    for path in reports:
        generator = path.name.removesuffix(".txt")
        result = verdict(read_text(path))
        if not result.ran_battery:
            broken.append(generator)
            continue
        expected = expectations.get(generator)
        if generator not in expectations:
            unknown.append(generator)
        if result.failed:
            (confirmed if expected == "fail" else failing).append(
                describe(generator, result.decisive)
            )
            continue
        if expected == "fail":
            inconclusive.append(generator)
        if result.marginal:
            marginal.append(describe(generator, result.marginal))

    print(f"[testu01] {len(reports)} generator(s) measured with SmallCrush.")
    if confirmed:
        print(
            f"[testu01] {len(confirmed)} recorded-weak generator(s) failed, as the manifest says they should:"
        )
        for line in confirmed:
            print(f"    {line}")
    if inconclusive:
        print(
            f"[testu01] {len(inconclusive)} recorded-weak generator(s) passed SmallCrush; it is the "
            f"shallower battery, so this is inconclusive rather than a fault: {', '.join(inconclusive)}"
        )
    if unknown:
        print(f"::error::[testu01] Not in the manifest: {', '.join(unknown)}", file=sys.stderr)
        print(
            "[testu01] A generator with no recorded expectation cannot be judged. Add it.",
            file=sys.stderr,
        )
        return 1
    if marginal:
        print(f"[testu01] {len(marginal)} marginal p-value(s), which is expected noise:")
        for line in marginal:
            print(f"    {line}")
    if broken:
        print(
            f"::error::[testu01] {len(broken)} report(s) contain no battery summary: {', '.join(broken)}",
            file=sys.stderr,
        )
        print(
            "[testu01] That is a harness fault -- the stream ran out, or the driver died. Not a pass.",
            file=sys.stderr,
        )
        return 1
    if failing:
        print(
            f"::error::[testu01] {len(failing)} generator(s) produced a decisive TestU01 failure.",
            file=sys.stderr,
        )
        for line in failing:
            print(f"    {line}", file=sys.stderr)
        print(
            "[testu01] A generator whose weakness is already recorded belongs in the manifest;",
            file=sys.stderr,
        )
        print(
            "[testu01] one that is not is a regression. Re-run with another --seed to confirm.",
            file=sys.stderr,
        )
        return 1
    print("[testu01] No unexpected decisive failures.")
    return 0


def check_control(path: Path) -> int:
    result = verdict(read_text(path))
    if not result.ran_battery:
        print(
            "[testu01] SELF-TEST INCONCLUSIVE: the control has no battery summary.", file=sys.stderr
        )
        return 1
    if not result.failed:
        print("[testu01] SELF-TEST FAILED: the control has no decisive failure.", file=sys.stderr)
        return 1
    print("[testu01] Self-test passed; the harness detects decisive failures:")
    for row in result.decisive:
        print(f"  {row.test}: {row.raw}")
    return 0


def main(arguments: list[str]) -> int:
    try:
        if len(arguments) == 2 and arguments[0] == "--check-control":
            return check_control(Path(arguments[1]))
        directory = arguments[0] if arguments else ""
        manifest = (
            arguments[1]
            if len(arguments) > 1
            else str(Path(__file__).with_name("expected-outcomes.json"))
        )
        return report(directory, manifest)
    except (OSError, ValueError, AttributeError, TypeError) as error:
        print(f"[testu01] {error}", file=sys.stderr)
        return 1


def configure_cli_output() -> None:
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, TextIOWrapper):
            stream.reconfigure(encoding="utf-8", newline="\n")


if __name__ == "__main__":
    configure_cli_output()
    sys.exit(main(sys.argv[1:]))
