"""Require Preserve on package attributes read by runtime reflection."""

from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NOT_OURS = {
    "ObsoleteAttribute": "System.ObsoleteAttribute",
    "JsonConverterAttribute": "System.Text.Json.Serialization.JsonConverterAttribute",
    "JsonIgnoreAttribute": "System.Text.Json.Serialization.JsonIgnoreAttribute",
    "JsonPropertyNameAttribute": "System.Text.Json.Serialization.JsonPropertyNameAttribute",
    "SerializeField": "UnityEngine.SerializeField",
}
REFLECTED_BASES = {
    "BaseRelationalComponentAttribute": (
        "BaseRelationalComponentAttribute reads it via IsAttributeDefined<TAttribute>"
    )
}
READ = r"(?:TryGetAttributeSafe|IsAttributeDefined|GetCustomAttributes?|IsDefined)\s*"
REFLECTED = re.compile(
    READ
    + r"(?:<\s*([A-Za-z0-9_]*Attribute)\s*>)?[^;]{0,300}?"
    + r"(?:out\s+([A-Za-z0-9_]*Attribute)\b|typeof\(\s*([A-Za-z0-9_]*Attribute)\s*\))",
    re.DOTALL,
)
GENERIC_READ = re.compile(READ + r"<\s*([A-Za-z0-9_]*Attribute)\s*>\s*\(")
DECLARATION = re.compile(r"^[ \t]*(?:public|internal)[^\r\n]*?\bclass\s+([A-Za-z0-9_]+Attribute)\b")
BASE = re.compile(r":\s*([A-Za-z0-9_]+)")
PRESERVE = re.compile(r"\[\s*Preserve\s*(?:\(|\])")


@dataclass(frozen=True)
class Declaration:
    file: str
    preserved: bool
    base_type: str | None


def attribute_block_above(lines: list[str], index: int) -> list[str]:
    block: list[str] = []
    depth = 0
    conditional_start = -1
    for line in reversed(lines[:index]):
        trimmed = line.strip()
        if depth > 0:
            block.append(line)
            depth += line.count("]") - line.count("[")
            continue
        if trimmed == "#endif" and conditional_start < 0:
            conditional_start = len(block)
            continue
        if conditional_start >= 0 and trimmed == "#if UNITY_5_3_OR_NEWER":
            conditional_start = -1
            continue
        if not trimmed or trimmed.startswith(("//", "*", "/*")):
            continue
        if trimmed.endswith("]"):
            block.append(line)
            depth = line.count("]") - line.count("[")
            continue
        if conditional_start >= 0:
            del block[conditional_start:]
        break
    if conditional_start >= 0:
        del block[conditional_start:]
    return block


def collect_sources(root: Path) -> list[Path]:
    if not root.is_dir():
        raise ValueError(f"Runtime source directory is missing: {root}")
    files: list[Path] = []

    def fail(error: OSError) -> None:
        raise error

    for directory, _children, names in os.walk(root, onerror=fail, followlinks=False):
        files.extend(Path(directory) / name for name in names if name.endswith(".cs"))
    return sorted(files)


def inspect_sources(files: list[Path]) -> tuple[dict[str, str], dict[str, Declaration]]:
    reflected: dict[str, str] = {}
    declarations: dict[str, Declaration] = {}
    for file in files:
        text = file.read_text(encoding="utf-8")
        relative = os.path.relpath(file, ROOT)
        for pattern in (REFLECTED, GENERIC_READ):
            for read in pattern.finditer(text):
                for name in read.groups():
                    if name and name not in {"TAttribute", "Attribute"}:
                        reflected.setdefault(name, relative)
        lines = text.splitlines()
        for index, line in enumerate(lines):
            match = DECLARATION.match(line)
            if match is None:
                continue
            base = BASE.search(line[match.end(1) :])
            declarations[match[1]] = Declaration(
                relative,
                any(PRESERVE.search(value) for value in attribute_block_above(lines, index)),
                base[1] if base is not None else None,
            )
    return reflected, declarations


def analyze(root: Path) -> tuple[list[str], list[str]]:
    reflected, declarations = inspect_sources(collect_sources(root))
    for name, candidate in declarations.items():
        reason = REFLECTED_BASES.get(candidate.base_type or "")
        if reason is not None:
            reflected.setdefault(name, reason)
    failures: list[str] = []
    checked: list[str] = []
    for name, used_in in sorted(reflected.items()):
        declaration = declarations.get(name)
        if declaration is None:
            if name not in NOT_OURS:
                failures.append(
                    f"{name} is read by reflection in {used_in} but is declared nowhere under "
                    "Runtime/. If it belongs to another library, add it to NOT_OURS in "
                    "scripts/lint_preserve_attributes.py with its full name."
                )
            continue
        checked.append(name)
        if not declaration.preserved:
            failures.append(
                f"{name} ({declaration.file}) is read by reflection at runtime from {used_in}, "
                "but is not marked [Preserve]. The IL2CPP linker may drop it, and the code "
                "reading it then silently falls back to its default."
            )
    return checked, failures


def main() -> int:
    root = Path(os.environ.get("PRESERVE_ATTRIBUTES_ROOT", str(ROOT / "Runtime"))).resolve()
    try:
        checked, failures = analyze(root)
    except (OSError, UnicodeError, ValueError) as error:
        print(
            f"[lint-preserve-attributes] Cannot scan runtime sources: {error}",
            file=sys.stderr,
        )
        return 1
    if not checked:
        print(
            "[lint-preserve-attributes] Found no runtime-reflected package attributes at all. "
            "That is almost certainly a broken scan rather than a clean repository.",
            file=sys.stderr,
        )
        for failure in failures:
            print(f"  FAIL {failure}", file=sys.stderr)
        return 1
    for name in checked:
        print(f"  OK   {name}")
    if failures:
        print(file=sys.stderr)
        for failure in failures:
            print(f"  FAIL {failure}", file=sys.stderr)
        print(
            f"\n[lint-preserve-attributes] {len(failures)} attribute(s) need [Preserve].",
            file=sys.stderr,
        )
        return 1
    print(
        f"\n[lint-preserve-attributes] {len(checked)} runtime-reflected attribute(s) are preserved."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
