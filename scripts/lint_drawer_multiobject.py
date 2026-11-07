"""Report advisory multi-object editing hazards in Unity property drawers."""

from __future__ import annotations

import argparse
import re
from dataclasses import dataclass
from pathlib import Path

from cli_encoding import configure_output, escape_command_data
from csharp_sources import is_link, mask_noise, source_files

ROOT = Path(__file__).resolve().parents[1]
VALUE = r"(intValue|stringValue|floatValue|boolValue|enumValueIndex|objectReferenceValue)"
WRITE = re.compile(r"property\." + VALUE + r"\s*=(?!=|>)")
INDEX_READ = re.compile(
    r"(Array\.IndexOf|IndexOf|\.Index\(|selectedIndex\s*=|currentIndex\s*=|index\s*=).*property\."
    + VALUE
    + r"\b"
)
CLAMP = re.compile(
    r"(selectedIndex|currentIndex|index)\s*=\s*(0|Math\.Max|Mathf\.Max|Mathf\.Clamp)"
)
SELECTED = re.compile(r"bool\s+isSelected\s*=\s*.+==\s*.*(?:Index|index)")
METHOD = re.compile(
    r"^\s*(?:public|private|protected|internal)?\s*(?:static)?\s*(?:override)?\s*"
    r"(?:void|VisualElement|\w[\w<>,\s]*)\s+(\w+)\s*\("
)
CALLBACK = re.compile(
    r"=>|delegate\s*\(|AddItem\s*\(|RegisterValueChangedCallback|onClick\s*\+?=|menu\.AddItem"
)
MIXED = "hasMultipleDifferentValues"
SUPPRESSION = "UNH-SUPPRESS"


@dataclass(frozen=True)
class Finding:
    """Retain the source line and advisory diagnostic."""

    line: int
    message: str


def containing_method(lines: list[str], line_index: int) -> str | None:
    """Find a containing signature by walking brace depth backwards from a one-based line."""
    depth = 0
    for line in reversed(lines[:line_index]):
        depth += line.count("}") - line.count("{")
        if depth <= 0:
            match = METHOD.search(line)
            if match:
                return match[1]
    return None


def nearby_check(lines: list[str], index: int, radius: int) -> bool:
    return any(MIXED in line for line in lines[max(0, index - radius) : index + radius + 1])


def analyze(source: str) -> list[Finding]:
    """Keep the existing advisory heuristics while ignoring comments and string literals."""
    lines = mask_noise(source).splitlines()
    comments_masked = mask_noise(source, preserve_strings=True).splitlines()
    original = source.splitlines()
    findings: list[Finding] = []
    for index, line in enumerate(lines):
        marker = original[index].find(SUPPRESSION)
        if marker >= 0 and comments_masked[index][marker : marker + len(SUPPRESSION)].isspace():
            continue
        write = WRITE.search(line)
        if write and not CALLBACK.search(line):
            method = containing_method(lines, index + 1)
            if (
                method
                and not method.startswith(("Apply", "Set", "Update", "Write"))
                and (
                    method
                    in (
                        "OnGUI",
                        "DrawPropertyLayout",
                        "DrawGenericMenuDropDown",
                        "DrawPopupDropDown",
                    )
                    or method.startswith("Draw")
                )
            ):
                findings.append(
                    Finding(
                        index + 1,
                        f"Property assignment during render: property.{write[1]} =",
                    )
                )
        read = INDEX_READ.search(line)
        if read and not nearby_check(lines, index, 15):
            findings.append(
                Finding(
                    index + 1,
                    "Property used in index calculation without hasMultipleDifferentValues check: "
                    f"property.{read[2]}",
                )
            )
        if CLAMP.search(line) and not nearby_check(lines, index, 10):
            findings.append(
                Finding(index + 1, "Index clamping without hasMultipleDifferentValues check")
            )
        if SELECTED.search(line) and MIXED not in line:
            findings.append(
                Finding(
                    index + 1,
                    "GenericMenu isSelected assigned without hasMultipleDifferentValues guard",
                )
            )
    return findings


def selected_files(repository: Path, paths: list[str] | None) -> list[Path]:
    if not repository.is_dir() or is_link(repository):
        raise ValueError(f"Drawer repository root is missing or linked: {repository}")
    files: list[Path]
    if not paths:
        for root in (repository, repository / "Editor", repository / "Editor/CustomDrawers"):
            if root.exists() and is_link(root):
                raise ValueError(f"Drawer source root is linked: {root}")
        files = source_files(repository, ("Editor/CustomDrawers",))
    else:
        files = []
        for name in paths:
            path = Path(name)
            if not path.is_absolute():
                path = repository / path
            path = path.absolute()
            if not path.is_relative_to(repository) or ".." in path.parts:
                raise ValueError(f"Drawer input is outside the repository: {name}")
            if not path.is_file() or is_link(path):
                raise ValueError(f"Drawer input is missing or linked: {name}")
            parent = path.parent
            while parent != repository:
                if is_link(parent):
                    raise ValueError(f"Drawer input has a linked parent: {name}")
                parent = parent.parent
            files.append(path)
    drawers = sorted({path for path in files if path.name.lower().endswith("drawer.cs")})
    if not drawers:
        raise ValueError("The scan found no property drawers; a pass would mean nothing.")
    return drawers


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--paths", nargs="+")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    repository: Path = args.repo_root.absolute()
    try:
        files = selected_files(repository, args.paths)
        total = 0
        for path in files:
            source = path.read_text(encoding="utf-8-sig")
            relative = escape_command_data(path.relative_to(repository).as_posix())
            for finding in analyze(source):
                print(f"[WARN] {relative}:{finding.line} - {finding.message}")
                total += 1
        if args.verbose:
            print(f"[lint-drawer-multiobject] Checked {len(files)} property drawers.")
        print(
            f"Found {total} potential multi-object editing issues (warnings only)."
            if total
            else "No potential multi-object editing issues found."
        )
        return 0
    except (OSError, UnicodeError, ValueError) as error:
        print(f"[lint-drawer-multiobject] {escape_command_data(str(error))}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
