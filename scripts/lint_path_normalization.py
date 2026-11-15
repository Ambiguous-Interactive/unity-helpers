"""Require the shared path normalizer in package C# sources."""

from __future__ import annotations

import sys
from pathlib import Path

from lint_comparison_direction import tokenize

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = ("Runtime", "Editor", "Tests", "Generator~")
IMPLEMENTATION = "Runtime/Core/Helper/PathHelper.cs"
ANCHORS = {
    "Runtime": IMPLEMENTATION,
    "Editor": "Editor/Tools/AnalyzerPolicyAPI.cs",
    "Tests": "Tests/Editor/Helper/HelpersTests.cs",
    "Generator~": "Generator~/WallstopStudios.UnityHelpers.CountingLoopAudit/Program.cs",
}
BACKSLASH_LITERALS = frozenset(
    (r"'\\'", r"'\u005c'", r"'\u005C'", r"'\x5c'", r"'\x5C'", r'"\\"', '@"\\"')
)
SLASH_LITERALS = frozenset(
    ("'/'", '"/"', '@"/"', r"'\u002f'", r"'\u002F'", r'"\u002f"', r'"\u002F"')
)


def find_manual_normalizations(source: str) -> list[int]:
    """Find direct Replace calls, excluding comments and literal text."""
    tokens = tokenize(source)
    return [
        token.line
        for index, token in enumerate(tokens)
        if token.text in (".", "?.")
        and index + 6 < len(tokens)
        and tokens[index + 1].text.lstrip("@") == "Replace"
        and tokens[index + 2].text == "("
        and tokens[index + 3].text in BACKSLASH_LITERALS
        and tokens[index + 4].text == ","
        and tokens[index + 5].text in SLASH_LITERALS
        and tokens[index + 6].text in (")", ",")
    ]


def scan(root: Path) -> int:
    failures = 0
    for source_root in SOURCE_ROOTS:
        files = sorted(
            file
            for file in (root / source_root).rglob("*.cs")
            if not any(part in ("bin", "obj") for part in file.relative_to(root).parts)
        )
        anchor = root / ANCHORS[source_root]
        if not files or anchor not in files:
            raise ValueError(f"Missing {source_root} C# census or anchor: {anchor}")
        implementation_sites = 0
        for file in files:
            relative = file.relative_to(root).as_posix()
            sites = find_manual_normalizations(file.read_text(encoding="utf-8-sig"))
            if relative == IMPLEMENTATION:
                implementation_sites = len(sites)
                continue
            for line in sites:
                print(f"{relative}:{line}: use SanitizePath() or PathHelper.Sanitize().")
                failures += 1
        if source_root == "Runtime" and implementation_sites != 1:
            raise ValueError("Expected exactly one shared path-normalizer implementation.")
        print(f"Path normalization examined {source_root}: {len(files)} C# files.")
    return int(failures != 0)


def main() -> int:
    try:
        return scan(ROOT)
    except (OSError, UnicodeError, ValueError) as error:
        print(f"Path normalization scan failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
