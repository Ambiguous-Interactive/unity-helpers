"""Check documentation namespaces and linker assembly names against declared sources."""

from __future__ import annotations

import json
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path

from cli_encoding import configure_output
from lint_asmdef import read_object

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = ("Runtime", "Editor", "Tests", "Generator~", "Samples~")
SKIPPED = frozenset({"obj", "bin", "node_modules", "Library", "artifacts"})
USING = re.compile(r"^\s*using\s+(WallstopStudios(?:\.[A-Za-z0-9_]+)*)\s*;")
ASSEMBLY = re.compile(r'<assembly\s+fullname="(WallstopStudios[^"]*)"')
NAMESPACE = re.compile(r"^[ \t]*namespace\s+([A-Za-z0-9_.]+)", re.MULTILINE)


@dataclass(frozen=True)
class Analysis:
    """Keep the scan counts alongside the findings so an empty scan cannot pass."""

    violations: list[str]
    usings: int
    assemblies: int
    namespaces: int
    documents: int


def files_under(root: Path, extensions: tuple[str, ...]) -> list[Path]:
    if not root.exists():
        return []
    found: list[Path] = []

    def fail(error: OSError) -> None:
        raise error

    for directory, children, files in os.walk(root, onerror=fail, followlinks=False):
        children[:] = sorted(
            child for child in children if child not in SKIPPED and not child.startswith(".")
        )
        found.extend(Path(directory) / name for name in sorted(files) if name.endswith(extensions))
    return found


def declared(root: Path) -> tuple[set[str], set[str]]:
    namespaces: set[str] = set()
    assemblies: set[str] = set()
    for source in SOURCE_ROOTS:
        for file in files_under(root / source, (".cs",)):
            for match in NAMESPACE.finditer(file.read_text(encoding="utf-8")):
                parts = match[1].split(".")
                namespaces.update(".".join(parts[:count]) for count in range(1, len(parts) + 1))
        for file in files_under(root / source, (".asmdef",)):
            try:
                definition = read_object(file)
                name = definition.get("name")
                if isinstance(name, str) and name:
                    assemblies.add(name)
            except (OSError, UnicodeError, ValueError, json.JSONDecodeError):
                continue
    return namespaces, assemblies


def analyze(root: Path) -> Analysis:
    namespaces, assemblies = declared(root)
    violations: list[str] = []
    using_count = 0
    assembly_count = 0
    documents = files_under(root / "docs", (".md",))
    for file in documents:
        relative = file.relative_to(root).as_posix()
        for number, line in enumerate(file.read_text(encoding="utf-8").split("\n"), 1):
            using = USING.match(line)
            if using is not None:
                using_count += 1
                if using[1] not in namespaces:
                    violations.append(
                        f"{relative}:{number}: 'using {using[1]};' names a namespace nothing "
                        "declares. A reader copying this example gets a compile error."
                    )
            for assembly in ASSEMBLY.finditer(line):
                assembly_count += 1
                if assembly[1] not in assemblies:
                    violations.append(
                        f'{relative}:{number}: <assembly fullname="{assembly[1]}"> names an '
                        "assembly no .asmdef declares. The linker preserves nothing under a "
                        "wrong name and reports nothing, so this surfaces as a stripped player "
                        "rather than as an error."
                    )
    return Analysis(violations, using_count, assembly_count, len(namespaces), len(documents))


def main() -> int:
    root = Path(os.environ.get("DOC_IDENTIFIER_ROOT", str(ROOT))).resolve()
    try:
        result = analyze(root)
    except (OSError, UnicodeError, ValueError) as error:
        print(f"[lint-doc-identifiers] Cannot scan sources: {error}", file=sys.stderr)
        return 1
    empty: list[str] = []
    if result.documents == 0:
        empty.append("no Markdown files under docs")
    if result.namespaces == 0:
        empty.append(f"no namespaces declared under {', '.join(SOURCE_ROOTS)}")
    if empty:
        print(
            f"[lint-doc-identifiers] {' and '.join(empty)}, so this run checked nothing.",
            file=sys.stderr,
        )
        return 1
    if result.violations:
        print(
            f"[lint-doc-identifiers] {len(result.violations)} documentation reference(s) "
            "do not resolve:",
            file=sys.stderr,
        )
        for violation in result.violations:
            print(f"  {violation}", file=sys.stderr)
        return 1
    print(
        f"[lint-doc-identifiers] {result.usings} package using directive(s) and "
        f"{result.assemblies} assembly reference(s) across {result.documents} document(s) "
        "all resolve."
    )
    return 0


if __name__ == "__main__":
    configure_output()
    sys.exit(main())
