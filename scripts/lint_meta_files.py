"""Check Unity companion coverage and bounded C# importer metadata without changing files."""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path, PurePosixPath
from typing import cast

import yaml
from yaml.nodes import MappingNode, Node, ScalarNode, SequenceNode

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
    except OSError as error:
        return f"Cannot read C# metadata: {error}"
    return csharp_meta_bytes_error(raw)


def csharp_meta_bytes_error(raw: bytes) -> str | None:
    if len(raw) > MAX_META_BYTES:
        return f"C# metadata exceeds the {MAX_META_BYTES}-byte validation limit"
    try:
        content = raw.decode("utf-8-sig")
    except UnicodeError as error:
        return f"Cannot decode C# metadata: {error}"
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
    try:
        loader = yaml.SafeLoader(content)
        try:
            document = loader.get_single_node()
        finally:
            loader.dispose()
    except (yaml.YAMLError, RecursionError) as error:
        return f"Invalid C# metadata YAML: {error}"
    if not isinstance(document, MappingNode):
        return "C# metadata must contain a YAML mapping"
    return yaml_duplicate_key_error(document)


def yaml_duplicate_key_error(document: Node) -> str | None:
    """Reject repeated scalar setting keys without expanding aliases."""
    pending: list[Node] = [document]
    visited: set[int] = set()
    while pending:
        node = pending.pop()
        identity = id(node)
        if identity in visited:
            continue
        visited.add(identity)
        if isinstance(node, MappingNode):
            keys: set[tuple[str, str]] = set()
            for key, value in cast("list[tuple[Node, Node]]", node.value):
                if isinstance(key, ScalarNode):
                    name = cast("str", key.value)
                    setting = (key.tag, name)
                    if setting in keys:
                        return f"Duplicate YAML mapping key: {name}"
                    keys.add(setting)
                pending.append(key)
                pending.append(value)
        elif isinstance(node, SequenceNode):
            pending.extend(cast("list[Node]", node.value))
    return None


def indexed_assets(root: Path) -> dict[str, tuple[str, str]]:
    result = subprocess.run(  # noqa: S603 - fixed read-only index inventory.
        [
            git_executable(),
            "-C",
            str(root),
            "ls-files",
            "--stage",
            "-z",
            "--",
            ":(icase)*.cs",
            ":(icase)*.cs.meta",
        ],
        capture_output=True,
        check=True,
        timeout=60,
    )
    entries: dict[str, tuple[str, str]] = {}
    for entry in result.stdout.split(b"\0"):
        if entry:
            header, path = entry.decode("utf-8").split("\t", 1)
            _, identity, stage = header.split()
            entries[os.path.normcase(path)] = (identity, stage)
    return entries


def indexed_meta_error(root: Path, identity: str, stage: str) -> str | None:
    if stage != "0":
        return "Unmerged staged C# metadata"
    git = git_executable()
    size = subprocess.run(  # noqa: S603 - immutable object from Git's index inventory.
        [git, "-C", str(root), "cat-file", "-s", identity],
        capture_output=True,
        check=True,
        timeout=60,
    )
    if int(size.stdout) > MAX_META_BYTES:
        return f"C# metadata exceeds the {MAX_META_BYTES}-byte validation limit"
    blob = subprocess.run(  # noqa: S603 - size-checked immutable Git object, never an index reread.
        [git, "-C", str(root), "cat-file", "blob", identity],
        capture_output=True,
        check=True,
        timeout=60,
    )
    return csharp_meta_bytes_error(blob.stdout)


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


def check_shapes(root: Path, paths: list[str], *, staged: bool = False) -> int:
    failed = False
    indexed = indexed_assets(root) if staged else {}
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
        if asset.suffix.casefold() != ".cs":
            continue
        source_present = asset.is_file()
        source_indexed = os.path.normcase(source) in indexed
        if not source_present and not source_indexed:
            continue
        meta = Path(f"{asset}.meta")
        if source_present:
            error = csharp_meta_error(meta)
            if error is not None:
                annotation(f"{source}.meta", "UNH007", error)
                failed = True
        entry = indexed.get(os.path.normcase(f"{source}.meta"))
        if source_indexed and entry is None:
            annotation(f"{source}.meta", "UNH007", "Missing staged C# companion")
            failed = True
        elif entry is not None:
            error = indexed_meta_error(root, *entry)
            if error is not None:
                annotation(f"{source}.meta", "UNH007", f"Invalid staged companion: {error}")
                failed = True
    return int(failed)


def git_executable() -> str:
    git = shutil.which("git")
    if git is None:
        raise OSError("Git is required to inventory Unity metadata")
    return git


def inventory(root: Path) -> list[str]:
    git = git_executable()
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
                annotation(
                    relative,
                    "UNH006",
                    f"Orphaned .meta file (missing source): {source}",
                )
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
    parser.add_argument("--staged", action="store_true", help="Also validate indexed companions")
    parser.add_argument(
        "--stdin-paths",
        action="store_true",
        help="Check C# shapes for a JSON path list",
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
            return check_shapes(arguments.root, cast("list[str]", paths), staged=arguments.staged)
        if arguments.staged:
            parser.error("--staged requires --stdin-paths")
        return check(arguments.root, verbose=arguments.verbose)
    except (OSError, UnicodeError, subprocess.SubprocessError, ValueError) as error:
        print(f"::error::Unity metadata validation failed: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
