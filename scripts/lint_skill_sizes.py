"""Check authored LLM instructions against the 199-line size limit."""

from __future__ import annotations

import argparse
import codecs
import os
import re
from pathlib import Path
from typing import Literal

from cli_encoding import configure_output, escape_command_data
from csharp_sources import is_link, linked_entry

ROOT = Path(__file__).resolve().parents[1]
CONTEXT = ".llm/context.md"
INDEX = ".llm/skills/index.md"
Severity = Literal["OK", "WARNING", "CRITICAL", "ERROR"]


def line_count(path: Path) -> int:
    """Match StreamReader line boundaries, including BOM-identified Unicode encodings."""
    raw = path.read_bytes()
    if raw.startswith((codecs.BOM_UTF32_LE, codecs.BOM_UTF32_BE)):
        encoding = "utf-32"
    elif raw.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE)):
        encoding = "utf-16"
    else:
        encoding = "utf-8-sig"
    text = raw.decode(encoding)
    if not text:
        return 0
    lines = re.split(r"\r\n|\r|\n", text)
    return len(lines) - int(lines[-1] == "")


def classify(count: int) -> Severity:
    """Classify a line count against the warning, critical, and hard size boundaries."""
    if count >= 200:
        return "ERROR"
    if count == 199:
        return "CRITICAL"
    return "WARNING" if count > 180 else "OK"


def check_path(repository: Path, path: Path) -> None:
    """Reject input paths outside the instruction tree or backed by links or special files."""
    if not path.is_file():
        raise ValueError(f"Instruction input is not a file: {path}")
    current = path
    while current != repository:
        if is_link(current):
            raise ValueError(f"Instruction input is linked: {path}")
        current = current.parent


def files_to_check(repository: Path, paths: list[str] | None) -> tuple[list[Path], bool]:
    """Select explicit instruction files or walk the complete supported instruction tree."""
    if not repository.is_dir() or is_link(repository):
        raise ValueError(f"Instruction repository root is missing or linked: {repository}")
    skills = repository / ".llm/skills"
    if not skills.is_dir() or is_link(skills) or is_link(repository / ".llm"):
        raise ValueError(f"Skills directory not found or linked: {skills}")
    selected: set[Path] = set()
    if paths:
        context = False
        for name in paths:
            path = Path(name.replace("\\", "/"))
            if not path.is_absolute():
                path = repository / path
            path = Path(os.path.abspath(path))  # noqa: PTH100 - normalize without following links.
            if not path.is_relative_to(repository):
                continue
            relative = path.relative_to(repository).as_posix().casefold()
            if relative == CONTEXT:
                context = True
            elif (
                relative != INDEX
                and relative.endswith(".md")
                and relative.startswith((".llm/skills/", ".llm/references/"))
                and (path.exists() or path.is_symlink())
            ):
                check_path(repository, path)
                selected.add(path)
        return sorted(selected), context
    pending = [repository / ".llm"]
    while pending:
        with os.scandir(pending.pop()) as entries:
            for entry in entries:
                if linked_entry(entry):
                    raise ValueError(f"Instruction input is linked: {entry.path}")
                if entry.is_dir(follow_symlinks=False):
                    if not entry.name.startswith("."):
                        pending.append(Path(entry.path))
                elif entry.name.lower().endswith(".md"):
                    if not entry.is_file(follow_symlinks=False):
                        raise ValueError(f"Instruction input is not a regular file: {entry.path}")
                    path = Path(entry.path)
                    relative = path.relative_to(repository).as_posix().casefold()
                    if relative not in (CONTEXT, INDEX):
                        selected.add(path)
    return sorted(selected), True


def report(path: Path, name: str, prefix: str, *, verbose: bool) -> tuple[Severity, int]:
    """Print a file size and workflow diagnostic, returning its severity and observed count."""
    count = line_count(path)
    level = classify(count)
    name = escape_command_data(name)
    if level == "ERROR":
        action = "reduce" if prefix == "[context-size]" else "split"
        print(f"{prefix} ERROR: {name}: {count} lines (max: 199) - MUST {action}")
    elif level == "CRITICAL":
        print(f"{prefix} CRITICAL: {name}: {count} lines - AT the 199 line limit! Must split")
    elif verbose:
        suffix = " (consider splitting)" if level == "WARNING" else ""
        print(f"{prefix} {level}: {name}: {count} lines{suffix}")
    return level, count


def main(argv: list[str] | None = None) -> int:
    """Run the size policy for explicit files or the repository and return its gate status."""
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--paths", nargs="+")
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--fail-on-critical", action="store_true")
    args = parser.parse_args(argv)
    repository: Path = Path(os.path.abspath(args.repo_root))  # noqa: PTH100 - preserve link identity.
    try:
        files, check_context = files_to_check(repository, args.paths)
        counts = dict.fromkeys(("OK", "WARNING", "CRITICAL", "ERROR"), 0)
        for path in files:
            level, _ = report(
                path, path.relative_to(repository).as_posix(), "[skill-sizes]", verbose=args.verbose
            )
            counts[level] += 1
        checked = len(files)
        context_level: Severity | None = None
        context_count = 0
        context = repository / CONTEXT
        if check_context:
            if context.exists() or context.is_symlink():
                check_path(repository, context)
                context_level, context_count = report(
                    context, "context.md", "[context-size]", verbose=args.verbose
                )
                checked += 1
            else:
                print(
                    f"[context-size] WARNING: context.md not found at: {escape_command_data(str(context))}"
                )
        print(f"[skill-sizes] Summary: {len(files)} files checked")
        if args.verbose or counts["CRITICAL"] or counts["ERROR"]:
            print(
                f"[skill-sizes]   OK: {counts['OK']}, Warnings: {counts['WARNING']}, Critical: {counts['CRITICAL']}, Errors: {counts['ERROR']}"
            )
        if check_context:
            print(
                f"[context-size] Summary: context.md checked ({context_count} lines)"
                if context_level
                else "[context-size] Summary: context.md not found"
            )
        if checked == 0:
            if args.paths:
                print("No existing authored instruction files selected; no size checks ran.")
                return 0
            raise ValueError("No authored instruction files were found; a pass would mean nothing")
        failed = bool(
            counts["ERROR"]
            or context_level == "ERROR"
            or (args.fail_on_critical and (counts["CRITICAL"] or context_level == "CRITICAL"))
        )
        print(
            "Some files exceed size limits - see errors above"
            if failed
            else "All files within size limits"
        )
        return int(failed)
    except (OSError, UnicodeError, ValueError) as error:
        print(f"[skill-sizes] ERROR: {escape_command_data(str(error))}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
