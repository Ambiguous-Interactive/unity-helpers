"""Add spelling words without creating duplicates across dictionary buckets."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

from cli_encoding import configure_output
from cspell_config import ROOT, buckets, read_config, write_config
from lint_cspell_config import check

BUCKETS = ("unity-terms", "csharp-terms", "package-terms", "tech-terms", "words")
WORD_WHITESPACE = re.compile(
    r"[\t-\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]"
)


def format_config(path: Path) -> None:
    node = shutil.which("node")
    formatter = ROOT / "scripts/run-prettier.js"
    if node is None or not formatter.is_file():
        print("[prettier] skipped: Node or repository formatter is unavailable", file=sys.stderr)
        return
    try:
        result = subprocess.run(  # noqa: S603 - fixed formatter and isolated pathname arguments.
            [node, str(formatter), "--write", "--", str(path)],
            cwd=ROOT,
            check=False,
            timeout=120,
        )
        print("[prettier] ok" if result.returncode == 0 else "[prettier] skipped: formatter failed")
    except (OSError, subprocess.TimeoutExpired) as error:
        print(f"[prettier] skipped: {error}", file=sys.stderr)


def add_words(path: Path, bucket: str, words: list[str], *, dry_run: bool = False) -> int:
    try:
        if bucket not in BUCKETS:
            raise ValueError(f"unknown bucket '{bucket}'.")
        raw, config = read_config(path)
        groups = buckets(config)
        target_name = "root:words" if bucket == "words" else f"dictionary:{bucket}"
        target = next((group for group in groups if group.name == target_name), None)
        if target is None:
            raise ValueError(f"Bucket '{bucket}' not found in dictionaryDefinitions")
        target_words = target.words()
        target.replace(target_words)
        added: list[str] = []
        existing: list[str] = []
        conflicts: list[tuple[str, list[str]]] = []
        for word in words:
            if not word or WORD_WHITESPACE.search(word):
                raise ValueError(f"refusing to add empty or whitespace-containing word: '{word}'")
            lower = word.lower()
            locations = [
                group.name
                for group in groups
                if any(candidate.lower() == lower for candidate in group.words())
            ]
            if locations:
                if all(location == target_name for location in locations):
                    existing.append(word)
                else:
                    conflicts.append((word, locations))
            else:
                target_words.append(word)
                added.append(word)
        if conflicts:
            for word, locations in conflicts:
                print(
                    f"Error: '{word}' already present in: {', '.join(locations)}. Refusing duplicate.",
                    file=sys.stderr,
                )
            return 1
        if not added:
            suffix = f" Already present in '{bucket}': {', '.join(existing)}" if existing else ""
            print(f"No new words to add.{suffix}")
            return 0
        if dry_run:
            print(f"[dry-run] Would add to '{bucket}': {', '.join(added)}")
            return 0
        write_config(path, raw, config)
        print(f"Added to '{bucket}': {', '.join(added)}")
        format_config(path)
        result = check(path)
        print(
            "[lint:spelling:config] ok"
            if result == 0
            else "[lint:spelling:config] skipped: check failed"
        )
        return 0
    except (OSError, UnicodeError, ValueError) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=ROOT / "cspell.json")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("bucket", choices=BUCKETS)
    parser.add_argument("words", nargs="+")
    try:
        args = parser.parse_intermixed_args(argv)
    except SystemExit as error:
        return 0 if error.code == 0 else 1
    return add_words(args.config, args.bucket, args.words, dry_run=args.dry_run)


if __name__ == "__main__":
    configure_output()
    sys.exit(main())
