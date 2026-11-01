"""Reject adjacent XML summaries that document one C# member more than once."""

from __future__ import annotations

import argparse
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_ROOTS = ("Runtime", "Editor", "Tests", "Generator~")
SKIPPED_DIRECTORIES = frozenset(("bin", "obj", "node_modules", ".git"))
SUMMARY_OPEN = re.compile(r"<summary(?:\s[^>]*?)?(?<!/)>")
CODE_OPEN = re.compile(r"<code(?:\s[^>]*?)?(?<!/)>", re.IGNORECASE)
CODE_CLOSE = re.compile(r"</code\s*>", re.IGNORECASE)


@dataclass
class MaskState:
    block_comment: bool = False
    verbatim_string: bool = False


@dataclass(frozen=True)
class Violation:
    line: int
    count: int
    first: str


def mask_noise(line: str, state: MaskState) -> str:
    masked: list[str] = []
    index = 0
    while index < len(line):
        character = line[index]
        pair = line[index : index + 2]
        if state.block_comment:
            if pair == "*/":
                state.block_comment = False
                masked.append("  ")
                index += 2
            else:
                masked.append(" ")
                index += 1
        elif state.verbatim_string:
            if pair == '""':
                masked.append("  ")
                index += 2
            else:
                if character == '"':
                    state.verbatim_string = False
                masked.append(" ")
                index += 1
        elif pair == "//":
            masked.append(line[index:])
            break
        elif pair == "/*":
            state.block_comment = True
            masked.append("  ")
            index += 2
        elif pair == '@"':
            state.verbatim_string = True
            masked.append("  ")
            index += 2
        elif character in ('"', "'"):
            quote = character
            masked.append(" ")
            index += 1
            while index < len(line):
                if line[index] == "\\":
                    masked.append("  ")
                    index += 2
                    continue
                closing = line[index] == quote
                masked.append(" ")
                index += 1
                if closing:
                    break
        else:
            masked.append(character)
            index += 1
    return "".join(masked)


def count_summary_openings(text: str, in_code: bool) -> tuple[int, bool]:
    count = 0
    position = 0
    while position < len(text):
        if in_code:
            closing = CODE_CLOSE.search(text, position)
            if closing is None:
                return count, True
            position = closing.end()
            in_code = False
        else:
            opening = CODE_OPEN.search(text, position)
            end = opening.start() if opening is not None else len(text)
            count += len(SUMMARY_OPEN.findall(text, position, end))
            if opening is None:
                break
            position = opening.end()
            in_code = True
    return count, in_code


def analyze_file(source: str) -> list[Violation]:
    state = MaskState()
    lines = re.split(r"\r\n|\r|\n", source)
    entries: list[tuple[str, str]] = []
    for line in lines:
        masked = mask_noise(line, state).strip()
        kind = "doc" if masked.startswith("///") else "code"
        if not masked:
            kind = "blank"
        elif masked.startswith(("[", "#")):
            kind = "interior"
        entries.append((kind, masked))
    violations: list[Violation] = []
    index = 0
    while index < len(entries):
        if entries[index][0] != "doc":
            index += 1
            continue
        start = index
        count = 0
        first = ""
        in_code = False
        cursor = index
        last_doc = index
        while cursor < len(entries):
            kind, text = entries[cursor]
            if kind == "doc":
                last_doc = cursor
                openings, in_code = count_summary_openings(text, in_code)
                if openings and count == 0:
                    first = text
                count += openings
            elif kind != "interior":
                break
            cursor += 1
        if count > 1:
            violations.append(Violation(start + 1, count, first))
        index = last_doc + 1
    return violations


def is_excluded(relative: str) -> bool:
    return relative == "Runtime/Utils/SevenZip" or relative.startswith("Runtime/Utils/SevenZip/")


def collect_files(roots: list[str], repository: Path = ROOT) -> list[Path]:
    files: set[Path] = set()
    for name in roots:
        root = repository / name
        if not root.is_dir():
            raise OSError(f"Source root is missing or is not a directory: {root}")

        def fail(error: OSError) -> None:
            raise error

        for directory, children, names in os.walk(root, onerror=fail):
            children[:] = sorted(child for child in children if child not in SKIPPED_DIRECTORIES)
            for file in sorted(names):
                if file.endswith(".cs"):
                    source = Path(directory) / file
                    try:
                        relative = source.relative_to(repository).as_posix()
                    except ValueError:
                        relative = source.as_posix()
                    if not is_excluded(relative):
                        files.add(source)
    return sorted(files)


def display_path(file: Path) -> str:
    try:
        return Path(os.path.relpath(file, ROOT)).as_posix()
    except ValueError:
        return file.as_posix()


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(arguments)
    configured = os.environ.get("XML_DOC_SUMMARY_ROOTS")
    roots: list[str] = (
        [entry for entry in configured.split(os.pathsep) if entry]
        if configured
        else list(DEFAULT_ROOTS)
    )
    try:
        files = collect_files(roots)
        if not files:
            print(
                f"[xml-doc-summaries] no C# files were found under {', '.join(roots)}, so this run checked nothing.",
                file=sys.stderr,
            )
            return 1
        reports: list[str] = []
        for file in files:
            reports.extend(
                f'  {display_path(file)}:{violation.line}: doc block opens <summary> {violation.count} times; the first is "{violation.first}"'
                for violation in analyze_file(file.read_text(encoding="utf-8-sig"))
            )
    except (OSError, UnicodeError) as error:
        print(f"[xml-doc-summaries] {error}", file=sys.stderr)
        return 1
    if reports:
        print(
            f"[xml-doc-summaries] {len(reports)} doc comment block(s) carry more than one <summary>. Delete the stale one, or move it onto the member it actually documents.",
            file=sys.stderr,
        )
        print("\n".join(reports), file=sys.stderr)
        return 1
    if args.verbose:
        print(f"[xml-doc-summaries] {len(files)} file(s) clean.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
