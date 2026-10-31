"""Keep command-line diagnostics in UTF-8 across redirected Windows streams."""

from __future__ import annotations

import sys
from io import TextIOWrapper


def configure_output() -> None:
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, TextIOWrapper):
            stream.reconfigure(encoding="utf-8", errors=stream.errors)
