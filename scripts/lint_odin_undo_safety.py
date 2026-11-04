"""Check Odin WeakTargets casts and Undo arguments without treating comments as code."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

from cli_encoding import configure_output
from csharp_sources import mask_noise, source_files

ROOT = Path(__file__).resolve().parents[1]
CAST = re.compile(
    r"(?:\(\s*UnityEngine\.Object\s*\[\s*\]\s*\)\s*[\w.]*WeakTargets|WeakTargets\s*\.Cast\s*<\s*UnityEngine\.Object\s*>)",
    re.IGNORECASE,
)
AS_CAST = re.compile(r"WeakTargets\s*\[.*\]\s+as\s+UnityEngine\.Object", re.IGNORECASE)
UNDO_CAST = re.compile(
    r"Undo\.RecordObjects\s*\(\s*(?:\(\s*UnityEngine\.Object\s*\[\s*\]\s*\)\s*[\w.]*WeakTargets|.*WeakTargets.*Cast\s*<\s*UnityEngine\.Object\s*>)",
    re.IGNORECASE,
)
SAFE = tuple(
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"is\s+UnityEngine\.Object\s+\w+\s*&&\s*\w+\s*!=\s*null",
        r"is\s+UnityEngine\.Object\s+\w+\s+and\s+not\s+null",
        r"Where\s*\(\s*\w+\s*=>\s*\w+\s*!=\s*null\s*\)",
        r"OfType\s*<\s*UnityEngine\.Object\s*>",
        r"\.FindAll\s*\(\s*\w+\s*=>\s*\w+\s*!=\s*null",
        r"serializedObject\.targetObjects",
    )
)
SUPPRESSION = re.compile(r"UNH-SUPPRESS")


def analyze(source: str) -> list[tuple[int, str]]:
    code = mask_noise(source).split("\n")
    comment_masked = mask_noise(source, preserve_strings=True).split("\n")
    raw = source.split("\n")
    findings: list[tuple[int, str]] = []
    for index, line in enumerate(code):
        if any(
            not comment_masked[index][match.start() : match.end()].strip()
            for match in SUPPRESSION.finditer(raw[index])
        ):
            continue
        if CAST.search(line):
            findings.append(
                (
                    index + 1,
                    "UNH006: Unsafe cast of WeakTargets to UnityEngine.Object[]. Filter live Unity objects before recording Undo.",
                )
            )
        if AS_CAST.search(line) and not any(
            pattern.search(nearby)
            for nearby in code[max(0, index - 10) : index + 11]
            for pattern in SAFE
        ):
            findings.append(
                (
                    index + 1,
                    "UNH006: Unsafe 'as UnityEngine.Object' cast on WeakTargets element. Use pattern matching with a null check.",
                )
            )
        if UNDO_CAST.search(line):
            findings.append(
                (
                    index + 1,
                    "UNH006: Undo.RecordObjects called with unsafely-cast WeakTargets. Pass a filtered array of live Unity objects.",
                )
            )
    return findings


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--paths", type=Path, nargs="+")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    repository: Path = args.repo_root
    try:
        paths: list[Path] | None = args.paths
        files = (
            sorted({path.resolve(strict=True) for path in paths if path.suffix == ".cs"})
            if paths
            else source_files(repository, ("Editor/CustomDrawers/Odin",))
        )
        if not files:
            raise ValueError("No C# files were checked; a pass would mean nothing.")
        failed = False
        for path in files:
            if args.verbose:
                print(f"[lint-odin-undo-safety] Scanning {path}")
            for line, message in analyze(path.read_text(encoding="utf-8-sig")):
                print(f"::error file={path.as_posix()},line={line}::{message}")
                failed = True
        if failed:
            return 1
        print(f"[lint-odin-undo-safety] No forbidden patterns found across {len(files)} files.")
        return 0
    except (OSError, UnicodeError, ValueError) as error:
        print(f"[lint-odin-undo-safety] {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
