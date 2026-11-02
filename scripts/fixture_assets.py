"""Shared identity-preserving metadata output for image fixtures."""

from __future__ import annotations

import os
import tempfile
from pathlib import Path

Color = tuple[int, int, int, int]


def write_metadata(path: Path, content: str) -> bool:
    """Publish complete metadata without replacing an existing path."""
    if path.exists() or path.is_symlink():
        return False
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            newline="\r\n",
            dir=path.parent,
            prefix=f".{path.name}.",
            suffix=".tmp",
            delete=False,
        ) as stream:
            temporary = Path(stream.name)
            stream.write(content)
        try:
            os.link(temporary, path)
        except FileExistsError:
            return False
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    print(f"Created: {path}")
    return True
