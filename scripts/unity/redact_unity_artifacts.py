"""Scrub Unity artifact directories before CI uploads them."""

from __future__ import annotations

import os
import stat
import sys
from collections import Counter
from io import TextIOWrapper
from pathlib import Path
from typing import TYPE_CHECKING

from credential_patterns import redact_credentials

if TYPE_CHECKING:
    from collections.abc import Iterator, Sequence

MAXIMUM_FILE_BYTES = 256 * 1024 * 1024
COMMAND = "uv run --quiet --project scripts/python~ --locked --no-dev python scripts/unity/redact_unity_artifacts.py"
LEGACY_COMMAND = "node scripts/unity/redact-unity-artifacts.js"
USAGE = f"""Usage: {COMMAND} <directory> [<directory>...]

Rewrites credential values in every text file under each directory before the tree is uploaded as a
CI artifact. Missing directories are skipped so one call can cover every test mode of a run.
"""


def list_files(root: Path) -> Iterator[Path]:
    with os.scandir(root) as entries:
        for entry in entries:
            path = Path(entry.path)
            if entry.is_dir(follow_symlinks=False):
                if sys.platform == "win32" and (
                    entry.stat(follow_symlinks=False).st_reparse_tag
                    == stat.IO_REPARSE_TAG_MOUNT_POINT
                ):
                    continue
                yield from list_files(path)
            elif entry.is_file(follow_symlinks=False):
                yield path


def redact_directory(root: Path, label: str) -> str:
    if not root.is_dir():
        raise ValueError(f"{root} is not a directory.")

    changed: list[str] = []
    skipped: list[str] = []
    totals: Counter[str] = Counter()
    for path in sorted(
        list_files(root),
        key=lambda path: str(path).encode("utf-16-be", "surrogatepass"),
    ):
        relative = path.relative_to(root).as_posix()
        size = path.stat().st_size
        if size > MAXIMUM_FILE_BYTES:
            skipped.append(
                f"{relative} was not scanned because it is {size} bytes, over the {MAXIMUM_FILE_BYTES} cap"
            )
            continue
        try:
            data = path.read_bytes()
        except OSError as error:
            skipped.append(f"{relative} was not scanned because it could not be read: {error}")
            continue
        if b"\0" in data:
            continue

        text, counts = redact_credentials(data.decode("utf-8", errors="replace"))
        if not counts:
            continue
        try:
            path.write_bytes(text.encode("utf-8"))
        except OSError as error:
            raise ValueError(
                f"{relative} contains credential material but could not be rewritten: {error}"
            ) from error
        changed.append(f"  {relative}: {', '.join(sorted(counts))}")
        totals.update(counts)

    if changed:
        by_kind = ", ".join(f"{name} x{count}" for name, count in sorted(totals.items()))
        summary = f"Redacted {len(changed)} file(s) under {label}: {by_kind}."
    else:
        summary = f"No credential material found under {label}."
    return "\n".join([summary, *changed, *(f"  WARNING: {warning}." for warning in skipped)])


def main(arguments: Sequence[str]) -> int:
    usage = (
        USAGE.replace(COMMAND, LEGACY_COMMAND, 1)
        if os.environ.get("UH_REDACTION_LEGACY_USAGE") == "1"
        else USAGE
    )
    roots: list[str] = []
    try:
        for argument in arguments:
            if argument in {"--help", "-h"}:
                print(usage, end="")
                return 0
            if argument.startswith("-"):
                raise ValueError(f"Unknown option {argument}.")
            roots.append(argument)
        if not roots:
            print(usage, end="")
            return 1
        for root in roots:
            path = Path(os.path.abspath(root))  # noqa: PTH100 - preserve lexical Node path resolution.
            if path.exists():
                print(redact_directory(path, root))
            else:
                print(f"Skipping {root}; it does not exist.")
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, TextIOWrapper):
            stream.reconfigure(encoding="utf-8", newline="\n")
    raise SystemExit(main(sys.argv[1:]))
