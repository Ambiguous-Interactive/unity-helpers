"""Keep command-line diagnostics in UTF-8 and safe for workflow-command logs."""

from __future__ import annotations

import sys
from io import TextIOWrapper


def escape_command_data(value: str) -> str:
    """Keep diagnostic data on one workflow-command line without changing literal escapes."""
    return value.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def escape_command_property(value: str) -> str:
    """Prevent diagnostic filenames from creating extra command properties."""
    return escape_command_data(value).replace(",", "%2C").replace(":", "%3A")


def configure_output() -> None:
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, TextIOWrapper):
            stream.reconfigure(encoding="utf-8", errors=stream.errors)
