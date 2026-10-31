"""Reject case-redundant spelling entries and report cross-bucket warnings."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from cli_encoding import configure_output
from cspell_config import (
    ROOT,
    buckets,
    case_redundant,
    cross_duplicates,
    deduplicate,
    read_config,
    write_config,
)

CHECKS = (
    "  1. Case-redundant entries (blocking)",
    "  2. Cross-dictionary duplicates (warning only)",
)


def check(path: Path, *, fix: bool = False) -> int:
    try:
        raw, config = read_config(path)
        groups = buckets(config)
        issues = 0
        if config.get("caseSensitive") is False:
            for group in groups:
                words = group.words()
                redundant = case_redundant(words)
                for canonical, repeated_words in redundant:
                    issues += 1
                    print(
                        f"  [{group.name}] Case-redundant: {', '.join([canonical, *repeated_words])} "
                        "(caseSensitive is false)"
                    )
                if fix and redundant:
                    group.replace(deduplicate(words))
        duplicates = cross_duplicates(groups)
        for word, locations in duplicates:
            print(
                f'  [cross-duplicate] (warning) "{word}" appears in: {", ".join(locations)}',
                file=sys.stderr,
            )
        warnings = len(duplicates)
        if issues and fix:
            write_config(path, raw, config)
            print(f"cspell.json: Fixed {issues} case-redundant entries. Run prettier to re-format.")
            return 0
        if issues:
            print()
        print("Checks enforced:")
        print("\n".join(CHECKS))
        if issues:
            print(f"\ncspell.json: {issues} error(s), {warnings} warning(s).")
            print("Run with --fix to auto-deduplicate case-redundant entries.")
            return 1
        if warnings:
            print(f"cspell.json: No errors. {warnings} warning(s) (cross-duplicates).")
        else:
            print("cspell.json: No issues found.")
        return 0
    except (OSError, UnicodeError, ValueError) as error:
        print(f"Error loading cspell.json: {error}", file=sys.stderr)
        return 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=ROOT / "cspell.json")
    parser.add_argument("--fix", action="store_true")
    args = parser.parse_args(argv)
    return check(args.config, fix=args.fix)


if __name__ == "__main__":
    configure_output()
    sys.exit(main())
