"""Verify that the published random-generator periods match their declarations."""

from __future__ import annotations

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from csharp_sources import literal_end, mask_noise

MINIMUM_GENERATORS = 20
GENERATOR_ATTRIBUTE = re.compile(r"\[\s*RandomGeneratorMetadata\s*\(")
PERIOD_ARGUMENT = re.compile(r"\s*(period\s*:)\s*")
DOC_PATH = Path("docs/features/utilities/random-generators.md")
SOURCE_DIR = Path("Runtime/Core/Random")


def attribute_period(source: str, code: str, start: int) -> str | None:
    depth = 0
    for end in range(start, len(code)):
        character = code[end]
        if character in "([{":
            depth += 1
            continue
        if depth and character in ")]}":
            depth -= 1
            continue
        if depth or character not in ",)]}":
            continue

        argument = PERIOD_ARGUMENT.fullmatch(code, start, end)
        if argument is not None:
            value = mask_noise(source[argument.end(1) : end], preserve_strings=True).strip()
            if (
                value.startswith('"')
                and value.endswith('"')
                and literal_end(value, 0) == len(value)
            ):
                return value[1:-1]
            return None
        if character != ",":
            return None
        start = end + 1
    return None


def declared_periods(directory: Path) -> tuple[dict[str, str], list[str]]:
    declared: dict[str, str] = {}
    unannotated: list[str] = []
    for path in sorted(directory.iterdir()):
        if path.suffix != ".cs" or path.stem == "RandomGeneratorMetadata":
            continue
        source = path.read_text(encoding="utf-8")
        if "RandomGeneratorMetadata" not in source:
            continue
        code = mask_noise(source)
        attribute = GENERATOR_ATTRIBUTE.search(code)
        if attribute is None:
            continue
        period = attribute_period(source, code, attribute.end())
        if period is None or not period.strip():
            unannotated.append(path.stem)
        else:
            declared[path.stem] = period
    return declared, unannotated


def cells(line: str) -> list[str]:
    return [cell.strip() for cell in line.strip().removeprefix("|").removesuffix("|").split("|")]


def documented_periods(markdown: str) -> dict[str, str | None] | None:
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
    period_column = headings.index("Period") if "Period" in headings else None
    documented: dict[str, str | None] = {}
    for line in lines[header_index + 2 :]:
        if not line.startswith("| `"):
            break
        row = cells(line)
        documented[row[0].replace("`", "")] = (
            row[period_column] if period_column is not None and period_column < len(row) else None
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
    drifted: list[str] = []
    for name in sorted(declared):
        actual = documented[name]
        if declared[name] != actual:
            observed = "undefined" if actual is None else actual
            drifted.append(f'{name}: source "{declared[name]}" vs docs "{observed}"')
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
