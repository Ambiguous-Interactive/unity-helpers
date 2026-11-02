"""Shared identity-preserving metadata output for image fixtures."""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from pathlib import Path

Color = tuple[int, int, int, int]


def write_metadata(path: Path, content: str) -> bool:
    try:
        stream = path.open("x", encoding="utf-8", newline="\r\n")
    except FileExistsError:
        return False
    with stream:
        stream.write(content)
    print(f"Created: {path}")
    return True
