#!/usr/bin/env python3
"""Validate documentation links while preserving the standalone command contract."""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path

EXCLUDE_DIRS = {".git", "node_modules", ".vs", ".vscode", "Library", "Temp"}


# Inline markdown link or image: ![alt](target "title") or [text](target "title")
INLINE_LINK_RE = re.compile(
    r"!?(?P<all>\[(?P<text>[^\]]+)\]\((?P<target>[^)\s]+)(?:\s+\"[^\"]*\")?\))"
)

# Reference-style link definitions: [id]: target "title"
REF_DEF_RE = re.compile(r"^\s*\[[^\]]+\]:\s*(?P<target>\S+)(?:\s+\"[^\"]*\")?\s*$")


def is_external(target: str) -> bool:
    return target.startswith(("http://", "https://", "mailto:", "tel:", "data:"))


def has_unencoded_chars(target: str) -> bool:
    # Only flag raw spaces or plus signs in the path/query/fragment
    return (" " in target) or ("+" in target)


def scan_file(path: str) -> int:
    issues = 0
    try:
        with Path(path).open("r", encoding="utf-8") as f:
            lines = f.readlines()
    except (OSError, UnicodeError) as error:
        print(f"Cannot read markdown source {path}: {error}", file=sys.stderr)
        return 1

    for idx, line in enumerate(lines, start=1):
        # Inline links/images
        for m in INLINE_LINK_RE.finditer(line):
            target = m.group("target").strip()
            if is_external(target):
                continue
            if has_unencoded_chars(target):
                issues += 1
                print(
                    f"{path}:{idx}: Unencoded character(s) in link target: '{target}'. Encode spaces as %20 and '+' as %2B."
                )

        # Reference-style link definitions
        reference_match = REF_DEF_RE.match(line)
        if reference_match:
            target = reference_match.group("target").strip()
            if not is_external(target) and has_unencoded_chars(target):
                issues += 1
                print(
                    f"{path}:{idx}: Unencoded character(s) in link definition: '{target}'. Encode spaces as %20 and '+' as %2B."
                )

    return issues


def main(root: str) -> int:
    if not Path(root).is_dir():
        print(f"Markdown scan directory not found: {root}", file=sys.stderr)
        return 1
    issues = 0
    checked = 0
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
        for filename in filenames:
            if filename.lower().endswith(".md"):
                checked += 1
                issues += scan_file(os.path.join(dirpath, filename))  # noqa: PTH118 - preserve diagnostic path spelling.
    if checked == 0:
        print(f"No markdown sources found: {root}", file=sys.stderr)
        return 1
    if issues:
        print(
            f"Found {issues} markdown link(s) with unencoded spaces or plus signs.", file=sys.stderr
        )
        print("Please URL-encode spaces as %20 and '+' as %2B in relative links.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    sys.exit(main(root))
