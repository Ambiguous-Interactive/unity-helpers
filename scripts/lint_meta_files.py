"""Check Unity companion coverage and bounded C# importer metadata without changing files."""

from __future__ import annotations

import argparse
import fnmatch
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path, PurePosixPath
from typing import cast

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = (
    "Runtime",
    "Editor",
    "Tests",
    "Samples~",
    "Shaders",
    "Styles",
    "URP",
    "docs",
    "scripts",
)
SOURCE_ROOT_KEYS = frozenset(name.casefold() for name in SOURCE_ROOTS)
EXCLUDED_DIRECTORIES = frozenset(
    (
        "node_modules",
        ".git",
        "obj",
        "bin",
        "Library",
        "Temp",
        ".pytest_cache",
        "__pycache__",
        ".mypy_cache",
    )
)
EXCLUDED_DIRECTORY_KEYS = frozenset(name.casefold() for name in EXCLUDED_DIRECTORIES)
EXCLUDED_FILES = (
    "*.meta",
    "package-lock.json",
    "Gemfile.lock",
    "*.tmp",
    ".gitkeep",
    ".DS_Store",
    "Thumbs.db",
    "*.pyc",
    "*.pyo",
    "*.swp",
    "*.swo",
)
MAX_META_BYTES = 1024 * 1024


def excluded(relative: str, *, directory: bool = False) -> bool:
    parts = PurePosixPath(relative).parts
    if not parts or relative.casefold() == "samples~":
        return True
    if any(part.casefold() in EXCLUDED_DIRECTORY_KEYS for part in parts):
        return True
    asset_parts = parts[1:] if parts[0].casefold() == "samples~" else parts
    if any(part.endswith("~") for part in asset_parts):
        return True
    return not directory and any(
        fnmatch.fnmatchcase(parts[-1].casefold(), pattern.casefold()) for pattern in EXCLUDED_FILES
    )


def csharp_meta_error(meta: Path) -> str | None:
    try:
        with meta.open("rb") as stream:
            raw = stream.read(MAX_META_BYTES + 1)
        if len(raw) > MAX_META_BYTES:
            return f"C# metadata exceeds the {MAX_META_BYTES}-byte validation limit"
        content = raw.decode("utf-8-sig")
    except (OSError, UnicodeError) as error:
        return f"Cannot read C# metadata: {error}"
    lines = content.splitlines()
    required = {
        "fileFormatVersion": r"fileFormatVersion: 2",
        "guid": r"guid: [0-9a-fA-F]{32}",
        "MonoImporter": r"MonoImporter:",
    }
    for field, pattern in required.items():
        entries = [index for index, line in enumerate(lines) if re.match(rf"{field}\s*:", line)]
        if len(entries) != 1 or not re.fullmatch(pattern, lines[entries[0]]):
            return f"Expected exactly one valid {field} field"
    importer = lines.index("MonoImporter:")
    children: list[str] = []
    for line in lines[importer + 1 :]:
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if not line.startswith((" ", "\t")):
            break
        children.append(line)
    if not children or not re.match(r" +[A-Za-z][A-Za-z0-9_]*:(?:[ \t]|$)", children[0]):
        return "MonoImporter must contain an indented mapping"
    other_importers = [
        line
        for line in lines
        if re.match(r"[A-Za-z]+Importer\s*:", line) and line != "MonoImporter:"
    ]
    if other_importers:
        return "C# metadata must use only MonoImporter"
    return None


def annotation(path: str, code: str, message: str) -> None:
    escaped_path = (
        path.replace("%", "%25")
        .replace("\r", "%0D")
        .replace("\n", "%0A")
        .replace(",", "%2C")
        .replace(":", "%3A")
    )
    escaped_message = message.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
    print(f"::error file={escaped_path}::{code}: {escaped_message}")


def check_shapes(root: Path, paths: list[str]) -> int:
    failed = False
    for relative in sorted(set(paths)):
        source = relative[:-5] if relative.casefold().endswith(".meta") else relative
        parts = PurePosixPath(source).parts
        if (
            not parts
            or parts[0].casefold() not in SOURCE_ROOT_KEYS
            or ".." in parts
            or excluded(source)
        ):
            continue
        asset = root / source
        if asset.suffix.casefold() != ".cs" or not asset.is_file():
            continue
        meta = Path(f"{asset}.meta")
        error = csharp_meta_error(meta)
        if error is not None:
            annotation(f"{source}.meta", "UNH007", error)
            failed = True
    return int(failed)


def inventory(root: Path) -> list[str]:
    git = shutil.which("git")
    if git is None:
        raise OSError("Git is required to inventory Unity metadata")
    result = subprocess.run(  # noqa: S603 - fixed read-only inventory, anchored to the repository.
        [git, "-C", str(root), "ls-files", "-z", "--", *SOURCE_ROOTS],
        capture_output=True,
        check=True,
        timeout=60,
    )
    return [path.decode("utf-8") for path in result.stdout.split(b"\0") if path]


def check(root: Path, *, verbose: bool = False) -> int:
    paths = inventory(root)
    if not paths:
        print("::error::No tracked Unity assets found in the metadata scan roots")
        return 1
    directories: set[str] = set()
    failed = False
    sources: list[str] = []
    for relative in paths:
        path = root / relative
        for parent in PurePosixPath(relative).parents:
            if str(parent) != "." and not excluded(str(parent), directory=True):
                directories.add(str(parent))
        if relative.casefold().endswith(".meta"):
            source = relative[:-5]
            if (
                not excluded(source, directory=(root / source).is_dir())
                and not (root / source).exists()
            ):
                annotation(relative, "UNH006", f"Orphaned .meta file (missing source): {source}")
                failed = True
        elif not excluded(relative):
            sources.append(relative)
            if not Path(f"{path}.meta").exists():
                annotation(relative, "UNH005", f"Missing .meta file for file: {relative}")
                failed = True
    for relative in sorted(directories):
        if (root / relative).is_dir() and not (root / f"{relative}.meta").exists():
            annotation(relative, "UNH005", f"Missing .meta file for directory: {relative}")
            failed = True
    failed |= bool(check_shapes(root, sources))
    if verbose:
        print(
            f"[lint-meta-files] Checked {len(paths)} tracked files and {len(directories)} directories"
        )
    print("Unity meta file lint failed." if failed else "All Unity meta files are valid.")
    return int(failed)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument(
        "--stdin-paths", action="store_true", help="Check C# shapes for a JSON path list"
    )
    arguments = parser.parse_args()
    try:
        if arguments.stdin_paths:
            paths: object = json.load(sys.stdin)
            if (
                not isinstance(paths, list)
                or not paths
                or not all(isinstance(path, str) for path in cast("list[object]", paths))
            ):
                parser.error("Expected a nonempty JSON list of paths")
            return check_shapes(arguments.root, cast("list[str]", paths))
        return check(arguments.root, verbose=arguments.verbose)
    except (OSError, UnicodeError, subprocess.SubprocessError, ValueError) as error:
        print(f"::error::Unity metadata validation failed: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
