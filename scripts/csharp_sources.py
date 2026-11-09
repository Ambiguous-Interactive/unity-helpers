"""Enumerate a required C# corpus without traversing generated or linked directories."""

from __future__ import annotations

import os
import stat
import sys
from pathlib import Path
from typing import TYPE_CHECKING, cast

if TYPE_CHECKING:
    from collections.abc import Iterator

EXCLUDED = frozenset(("node_modules", ".git", "obj", "bin", "Library", "Temp"))


def is_link(path: Path) -> bool:
    info = path.lstat()
    attributes = cast("int", getattr(info, "st_file_attributes", 0))
    return stat.S_ISLNK(info.st_mode) or bool(attributes & 0x400)


def linked_entry(entry: os.DirEntry[str]) -> bool:
    if entry.is_symlink():
        return True
    attributes = 0
    if sys.platform == "win32":
        attributes = cast(
            "int", getattr(entry.stat(follow_symlinks=False), "st_file_attributes", 0)
        )
    return bool(attributes & 0x400)


def source_files(repository: Path, roots: tuple[str, ...]) -> list[Path]:
    files: list[Path] = []
    for name in roots:
        root = repository / name
        if not root.is_dir() or is_link(root):
            raise ValueError(f"Source root not found or linked: {name}")
        pending = [str(root)]
        while pending:
            with os.scandir(pending.pop()) as entries:
                for entry in entries:
                    if entry.name in EXCLUDED or linked_entry(entry):
                        continue
                    if entry.is_dir(follow_symlinks=False):
                        pending.append(entry.path)
                    elif entry.name.endswith(".cs"):
                        if not entry.is_file(follow_symlinks=False):
                            raise ValueError(
                                f"Source is missing or not a regular file: {entry.path}"
                            )
                        files.append(Path(entry.path))
    if not files:
        raise ValueError("The scan found no C# files; a pass would mean nothing.")
    return sorted(files)


def source_tokens(source: str) -> Iterator[tuple[int, int, str]]:
    """Yield literal, comment and directive spans without changing source offsets."""
    index = 0
    length = len(source)
    while index < length:
        start = index
        kind = "line"
        if source.startswith("//", index) or (
            source[index] == "#" and not source[source.rfind("\n", 0, index) + 1 : index].strip()
        ):
            kind = "directive" if source[index] == "#" else "line"
            end = source.find("\n", index)
            index = length if end < 0 else end
        elif source.startswith("/*", index):
            kind = "block"
            end = source.find("*/", index + 2)
            index = length if end < 0 else end + 2
        elif source[index] in ('"', "'"):
            kind = "literal"
            quote = source[index]
            verbatim = (
                quote == '"'
                and index > 0
                and (source[index - 1] == "@" or source[max(0, index - 2) : index] == "@$")
            )
            raw = 0
            while quote == '"' and index + raw < length and source[index + raw] == quote:
                raw += 1
            if raw >= 3 and not verbatim:
                end = source.find('"' * raw, index + raw)
                index = length if end < 0 else end + raw
            else:
                index += 1
                while index < length:
                    if not verbatim and source[index] in "\r\n":
                        break
                    if source[index] == quote:
                        index += 1
                        if verbatim and index < length and source[index] == quote:
                            index += 1
                            continue
                        break
                    if not verbatim and source[index] == "\\":
                        if index + 1 < length and source[index + 1] in "\r\n":
                            index += 1
                            break
                        index += 1
                    index += 1
        else:
            index += 1
            continue
        yield start, min(index, length), kind


def mask_noise(source: str, *, preserve_strings: bool = False) -> str:
    """Keep offsets and line breaks while removing comments and literal contents."""
    result = list(source)
    for start, end, kind in source_tokens(source):
        if kind == "literal" and preserve_strings:
            continue
        for position in range(start, end):
            if source[position] not in "\r\n":
                result[position] = " "
    return "".join(result)
