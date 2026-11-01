"""Validate changelog sections and concise, contiguous unreleased entries."""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from datetime import date
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
CHANGE_TYPES = ("Added", "Changed", "Deprecated", "Removed", "Fixed", "Security", "Improved")


@dataclass
class Report:
    errors: list[str] = field(default_factory=list[str])
    warnings: list[str] = field(default_factory=list[str])
    versions: list[tuple[str, int]] = field(default_factory=list[tuple[str, int]])
    change_types: int = 0
    lines: int = 0


def rendered_entry(entry: str) -> str:
    text = re.sub(r"^[-*]\s+", "", entry.strip())
    text = re.sub(r"\s*\(\[#\d+\]\([^)]*\)(\s*,\s*\[#\d+\]\([^)]*\))*\)\.?$", "", text)
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    return re.sub(r"[`*]", "", text).strip()


def utf16_length(text: str) -> int:
    return len(text.encode("utf-16-le")) // 2


def analyze(source: str, maximum_entry_length: int = 300) -> Report:
    lines = source.splitlines()
    report = Report(lines=len(lines))
    if lines and not re.match(r"^#\s+Changelog", lines[0], re.IGNORECASE):
        report.warnings.append("Line 1: Expected '# Changelog' as the first line")
    unreleased_line = 0
    has_subsections = False
    section = ""
    previous_indent: str | None = None
    blank_since_bullet = False
    for number, line in enumerate(lines, 1):
        if re.match(r"^#{2,3}\s", line):
            previous_indent = None
            blank_since_bullet = False
        if re.fullmatch(r"##\s+\[Unreleased\]\s*", line, re.IGNORECASE):
            unreleased_line = number
            section = "Unreleased"
            continue
        version_match = re.match(r"^##\s+\[(\d+\.\d+\.\d+)\]", line)
        if version_match:
            version = version_match[1]
            date_match = re.fullmatch(r"##\s+\[\d+\.\d+\.\d+\]\s+-\s+(\d{4}-\d{2}-\d{2})\s*", line)
            if date_match:
                try:
                    date.fromisoformat(date_match[1])
                except ValueError:
                    report.errors.append(
                        f"Line {number}: Invalid date format '{date_match[1]}' - expected YYYY-MM-DD"
                    )
            elif re.fullmatch(r"##\s+\[\d+\.\d+\.\d+\]\s*", line):
                report.warnings.append(
                    f"Line {number}: Version [{version}] has no date - recommended format is '## [{version}] - YYYY-MM-DD'"
                )
            else:
                report.errors.append(
                    f"Line {number}: Malformed version header - expected '## [{version}] - YYYY-MM-DD' or '## [{version}]'"
                )
            report.versions.append((version, number))
            section = version
            continue
        change_match = re.fullmatch(r"###\s+(\w+)\s*", line)
        if change_match:
            report.change_types += 1
            if section == "Unreleased":
                has_subsections = True
            change_type = change_match[1]
            if change_type.casefold() not in {name.casefold() for name in CHANGE_TYPES}:
                report.warnings.append(
                    f"Line {number}: Non-standard change type '### {change_type}' - standard types are: {', '.join(CHANGE_TYPES)}"
                )
            continue
        if re.match(r"^##[^\s]|^###[^\s]", line):
            report.errors.append(f"Line {number}: Malformed header - missing space after ## or ###")
        if re.fullmatch(r"\s*[-*]\s*", line):
            report.errors.append(
                f"Line {number}: Empty bullet list item detected. Remove the stray list marker or add entry text."
            )
        bullet = re.match(r"^(\s*)[-*]\s+\S", line)
        if section == "Unreleased":
            if bullet:
                rendered = rendered_entry(line)
                length = utf16_length(rendered)
                if length > maximum_entry_length:
                    report.errors.append(
                        f"Line {number}: Entry is {length} rendered characters, over the {maximum_entry_length} limit. Lead with the user-visible effect in one or two sentences and move the explanation into a docs/ guide: {rendered[:80]}..."
                    )
                indent = bullet[1]
                if blank_since_bullet and indent == previous_indent:
                    report.errors.append(
                        f"Line {number}: Blank line between two entries in the same list. Remove it so the section stays one list."
                    )
                previous_indent = indent
                blank_since_bullet = False
            elif not line.strip():
                if previous_indent is not None:
                    blank_since_bullet = True
            else:
                previous_indent = None
                blank_since_bullet = False
        if re.match(r"^##\s+\d+\.\d+\.\d+", line):
            report.warnings.append(
                f"Line {number}: Version should be in brackets, e.g., '## [1.0.0] - YYYY-MM-DD'"
            )
    if not unreleased_line:
        report.errors.append("Missing required '## [Unreleased]' section")
    else:
        for version, number in report.versions:
            if number < unreleased_line:
                report.errors.append(
                    f"Line {number}: Version [{version}] appears before [Unreleased] section (line {unreleased_line})"
                )
        if not has_subsections:
            report.warnings.append(
                f"Line {unreleased_line}: [Unreleased] section has no change type subsections (e.g., ### Added)"
            )
    grouped: dict[str, list[int]] = {}
    for version, number in report.versions:
        grouped.setdefault(version, []).append(number)
    for version, numbers in grouped.items():
        if len(numbers) > 1:
            report.errors.append(
                f"Duplicate version [{version}] found at lines: {', '.join(str(number) for number in numbers)}"
            )
    for (previous, previous_line), (current, number) in zip(
        report.versions, report.versions[1:], strict=False
    ):
        if tuple(map(int, current.split("."))) > tuple(map(int, previous.split("."))):
            report.warnings.append(
                f"Line {number}: Version [{current}] is newer than [{previous}] (line {previous_line}) - versions should be in descending order"
            )
    return report


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verbose", "-VerboseOutput", action="store_true")
    parser.add_argument("--max-entry-length", "-MaxEntryLength", type=int, default=300)
    parser.add_argument(
        "--changelog-path", "-ChangelogPath", type=Path, default=ROOT / "CHANGELOG.md"
    )
    args = parser.parse_args(arguments)
    path: Path = args.changelog_path
    try:
        report = analyze(path.read_text(encoding="utf-8-sig"), args.max_entry_length)
    except (OSError, UnicodeError) as error:
        print(f"[changelog-lint] ERROR: CHANGELOG.md not found or unreadable at: {path}: {error}")
        return 1
    print("\n" + "=" * 60)
    if report.warnings:
        print(f"\n[changelog-lint] WARNING: Found {len(report.warnings)} warning(s):")
        print("\n".join(f"  - {warning}" for warning in report.warnings))
    if report.errors:
        print(f"\n[changelog-lint] ERROR: Found {len(report.errors)} error(s):")
        print("\n".join(f"  - {error}" for error in report.errors))
        print("\n[changelog-lint] ERROR: CHANGELOG.md validation FAILED")
        return 1
    print("\n[changelog-lint] CHANGELOG.md validation passed!")
    if args.verbose:
        print(
            f"[changelog-lint] Checked: {report.lines} lines, {len(report.versions)} version(s), {report.change_types} change type header(s)"
        )
    print("=" * 60)
    return 0


if __name__ == "__main__":
    sys.exit(main())
