"""Verify that the published random-generator periods match their declarations."""

from __future__ import annotations

import re
import sys
from pathlib import Path

MINIMUM_GENERATORS = 20
GENERATOR_ATTRIBUTE = "[RandomGeneratorMetadata("
PERIOD = re.compile(r'\[RandomGeneratorMetadata\(.*?period:\s*"([^"]*)"', re.S)
DOC_PATH = Path("docs/features/utilities/random-generators.md")
SOURCE_DIR = Path("Runtime/Core/Random")


def declared_periods(directory: Path) -> tuple[dict[str, str], list[str]]:
    declared: dict[str, str] = {}
    unannotated: list[str] = []
    for path in sorted(directory.iterdir()):
        if path.suffix != ".cs" or path.stem == "RandomGeneratorMetadata":
            continue
        source = path.read_text(encoding="utf-8")
        if GENERATOR_ATTRIBUTE not in source:
            continue
        match = PERIOD.search(source)
        if match is None or not match[1].strip():
            unannotated.append(path.stem)
        else:
            declared[path.stem] = match[1]
    return declared, unannotated


def cells(line: str) -> list[str]:
    return [cell.strip() for cell in line.strip().removeprefix("|").removesuffix("|").split("|")]


def documented_periods(markdown: str) -> dict[str, str] | None:
    lines = markdown.split("\n")
    header_index = next(
        (
            index
            for index, line in enumerate(lines)
            if line.startswith("| Generator ") and "Period" in line
        ),
        None,
    )
    if header_index is None:
        return None

    headings = cells(lines[header_index])
    if "Period" not in headings:
        return None
    period_column = headings.index("Period")
    documented: dict[str, str] = {}
    for line in lines[header_index + 2 :]:
        if not line.startswith("| `"):
            break
        row = cells(line)
        documented[row[0].replace("`", "")] = (
            row[period_column] if period_column < len(row) else "undefined"
        )
    return documented


def check(description: str, condition: bool, details: str) -> None:
    if not condition:
        raise ValueError(f"{description}: {details}")
    print(f"  [PASS] {description}")


def run(repository: Path) -> None:
    print("Testing that every generator declares a period and the docs carry it...\n")
    declared, unannotated = declared_periods(repository / SOURCE_DIR)
    count = len(declared) + len(unannotated)
    check(
        f"at least {MINIMUM_GENERATORS} annotated generators were found",
        count >= MINIMUM_GENERATORS,
        f"found {count}; a scan that stops matching reads like a clean run",
    )
    check(
        "every annotated generator declares a period",
        not unannotated,
        f"these carry [RandomGeneratorMetadata] with no period: {', '.join(unannotated)}. "
        'Add period: "<published spec>" or period: "unpublished; N/M state bits live (measured)".',
    )

    documented = documented_periods((repository / DOC_PATH).read_text(encoding="utf-8"))
    check(
        "the documentation carries a Period column",
        documented is not None,
        f"{DOC_PATH.as_posix()} has no generator table with a Period column",
    )
    if documented is None:
        return

    missing = sorted(declared.keys() - documented.keys())
    check(
        "every generator appears in the documentation table",
        not missing,
        f"undocumented: {', '.join(missing)}",
    )
    unknown = sorted(documented.keys() - declared.keys())
    check(
        "the documentation table names no generator that does not exist",
        not unknown,
        f"documented but not found in Runtime/Core/Random: {', '.join(unknown)}",
    )
    drifted = [
        f'{name}: source "{declared[name]}" vs docs "{documented[name]}"'
        for name in sorted(declared)
        if declared[name] != documented[name]
    ]
    check(
        "every documented period matches the one declared on the type",
        not drifted,
        "; ".join(drifted),
    )
    print("\n6 passed, 0 failed.")


def main() -> int:
    try:
        run(Path(__file__).resolve().parents[2])
    except (OSError, UnicodeError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
