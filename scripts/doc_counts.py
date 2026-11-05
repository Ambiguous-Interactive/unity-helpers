"""Generate, synchronize, or validate the documented source counts."""

from __future__ import annotations

import argparse
import codecs
import json
import re
import sys
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path

from cli_encoding import configure_output
from csharp_sources import is_link, mask_noise, source_files

ROOT = Path(__file__).resolve().parents[1]
TARGETS = (
    "README.md",
    "llms.txt",
    "_config.yml",
    "index.md",
    ".llm/context.md",
    ".llm/skills/documentation-consistency.md",
    "docs/readme.md",
    "docs/index.md",
    "docs/overview/getting-started.md",
    "docs/overview/roadmap.md",
    "docs/overview/index.md",
    "docs/features/utilities/random-generators.md",
    "docs/features/editor-tools/editor-tools-guide.md",
    "docs/features/inspector/inspector-overview.md",
    "docs/images/unity-helpers-banner.svg",
)
EXCLUDED_RANDOM_TYPES = frozenset(
    name.casefold()
    for name in (
        "IRandom",
        "AbstractRandom",
        "PRNG",
        "ThreadLocalRandom",
        "RandomGeneratorMetadata",
        "RandomState",
        "RandomUtilities",
        "RandomComparer",
        "RandomSpeedBucket",
        "PerlinNoise",
    )
)
TEST = re.compile(
    r"^\s*\[(?:(?:Test|UnityTest)\]|TestCase\(|TestCaseSource\()",
    re.IGNORECASE | re.MULTILINE,
)
RANDOM = re.compile(
    r"(?:sealed\s+)?(?:public\s+)?(?:class|struct)\s+\w+[^{]*?(?:AbstractRandom|IRandom)",
    re.IGNORECASE,
)
TOOL = re.compile(r":\s*(?:EditorWindow|ScriptableWizard)|\[MenuItem\(", re.IGNORECASE)
REPLACEMENTS = (
    (
        "tests",
        re.compile(r"(\d[\d,]*)\+(\s+(?:automated\s+)?test(?:s?\b|\s+cases?\b))", re.IGNORECASE),
    ),
    ("randoms", re.compile(r"\b(\d+)\+?(\s+PRNG\s+implementations?\b)")),
    (
        "randoms",
        re.compile(r"\b(\d+)\+?(\s+high-performance\s+PRNG\s+implementations?\b)"),
    ),
    (
        "randoms",
        re.compile(r"\b(\d+)\+?(\s+high-performance\s+pseudo-random\s+number\s+generators?\b)"),
    ),
    ("randoms", re.compile(r"\b(\d+)\+?(\s+high-quality\s+RNG\s+implementations?\b)")),
    ("randoms", re.compile(r"\b(\d+)\+?(\s+high-quality\s+PRNGs?\b)")),
    (
        "randoms",
        re.compile(r"\b(\d+)\+?(\s+high-quality\s+random\s+number\s+generators?\b)"),
    ),
    ("tools", re.compile(r"\b(\d+)\+(\s+editor\s+tools?\b)")),
    ("tools", re.compile(r"\b(\d+)\+(\s+automation\s+tools?\b)")),
    ("tools", re.compile(r"\b(\d+)\+(\s+tools?\s+(?:for|that)\b)")),
    ("tools", re.compile(r"\b(\d+)\+(\s+sprite/texture/prefab\s+utilities?\b)")),
)


def display_count(exact: int) -> str:
    step = 5 if exact < 20 else 10 if exact < 100 else 100 if exact < 1000 else 1000
    rounded = exact // step * step
    return f"{rounded if rounded else exact:,}+"


def read_text(path: Path) -> str:
    content = path.read_bytes()
    encoding = (
        "utf-32"
        if content.startswith((codecs.BOM_UTF32_LE, codecs.BOM_UTF32_BE))
        else "utf-16"
        if content.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE))
        else "utf-8-sig"
    )
    return content.decode(encoding)


@dataclass(frozen=True)
class Counts:
    tests: int
    randoms: int
    tools: int

    def metadata(self) -> dict[str, object]:
        return {
            "generatedAt": datetime.now(UTC).strftime("%Y-%m-%d %H:%M:%S UTC"),
            "generatedBy": "scripts/doc_counts.py",
            **{
                key: {"exact": value, "display": display_count(value)}
                for key, value in (
                    ("testCount", self.tests),
                    ("prngCount", self.randoms),
                    ("editorToolCount", self.tools),
                )
            },
        }


def collect(repository: Path) -> Counts:
    roots = ("Tests", "Runtime/Core/Random", "Editor")
    totals = [0, 0, 0]
    for path in source_files(repository, roots):
        relative = path.relative_to(repository).as_posix()
        text = mask_noise(read_text(path).replace("\r\n", "\n").replace("\r", "\n"))
        if relative.startswith("Tests/"):
            totals[0] += len(TEST.findall(text))
        elif relative.startswith("Runtime/Core/Random/"):
            totals[1] += int(
                path.stem.casefold() not in EXCLUDED_RANDOM_TYPES
                and RANDOM.search(text) is not None
            )
        else:
            totals[2] += int(TOOL.search(text) is not None)
    return Counts(*totals)


def synchronized(text: str, counts: Counts) -> str:
    displays = {
        "tests": display_count(counts.tests),
        "randoms": display_count(counts.randoms),
        "tools": display_count(counts.tools),
    }
    for kind, pattern in REPLACEMENTS:
        display = displays[kind]

        def replacement(match: re.Match[str], selected_display: str = display) -> str:
            return selected_display + match[2]

        text = pattern.sub(replacement, text)
    return text


def sync(repository: Path, counts: Counts, *, check: bool) -> int:
    subjects = 0
    updates: list[tuple[str, Path, str]] = []
    root = repository.resolve()
    for relative in TARGETS:
        path = repository
        for part in Path(relative).parts:
            path = path / part
            if (path.exists() or path.is_symlink()) and is_link(path):
                raise ValueError(f"Documentation target has a linked path component: {relative}")
        if not path.resolve().is_relative_to(root):
            raise ValueError(f"Documentation target is linked outside its allowed path: {relative}")
        if not path.exists():
            continue
        if not path.is_file():
            raise ValueError(f"Documentation target is linked or not a regular file: {relative}")
        subjects += 1
        original = read_text(path)
        updated = synchronized(original, counts)
        if original == updated:
            continue
        updates.append((relative, path, updated))
    if not subjects:
        raise ValueError("No documentation targets found; a pass would mean nothing.")
    for relative, path, updated in updates:
        if check:
            print(f"  MISMATCH: {relative}")
        else:
            path.write_text(updated.rstrip() + "\n", encoding="utf-8", newline="\n")
            print(f"  Updated: {relative}")
    if check and updates:
        print(
            "Documentation counts are out of sync. Run: uv run --project scripts/python~ python scripts/doc_counts.py sync"
        )
        return 1
    print(
        "All documentation counts are in sync." if check else "Documentation counts synchronized."
    )
    return 0


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("generate", "sync", "check"))
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    args = parser.parse_args(argv)
    repository: Path = args.repo_root
    try:
        counts = collect(repository)
        for name, value in (
            ("Tests", counts.tests),
            ("PRNGs", counts.randoms),
            ("Editor tools", counts.tools),
        ):
            print(
                f"[doc-metadata] {name}: {value} exact -> {display_count(value)}",
                file=sys.stderr,
            )
        if args.mode == "generate":
            print(json.dumps(counts.metadata(), indent=2))
            return 0
        return sync(repository, counts, check=args.mode == "check")
    except (OSError, UnicodeError, ValueError) as error:
        print(f"[doc-counts] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
