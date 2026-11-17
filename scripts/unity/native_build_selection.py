"""Validate hosted Standalone diagnostic inputs before licensed jobs are admitted."""

from __future__ import annotations

import argparse
import os
import sys
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Mapping

DIRECT_FIELDS = (
    ("UH_NATIVE_DIAGNOSTICS", "true"),
    ("UH_NATIVE_MODE", "standalone"),
    ("UH_NATIVE_ACCEPTANCE", "none"),
)
HEAP_FIELDS = (*DIRECT_FIELDS, ("UH_NATIVE_DIRECT_CONTROL", "false"))
ATTACH_FIELDS = (*HEAP_FIELDS, ("UH_NATIVE_STANDARD_HEAP", "false"))
STARTUP_FIELDS = (("UH_NATIVE_ATTACH_CONTROL", "true"), ("UH_NATIVE_ATTACH_AFTER_STARTUP", "true"))
SHUTDOWN_FIELDS = (
    ("UH_NATIVE_ATTACH_CONTROL", "true"),
    ("UH_NATIVE_ATTACH_AFTER_STARTUP", "false"),
    ("UH_NATIVE_ATTACH_AFTER_SHUTDOWN", "true"),
)


def matches(environment: Mapping[str, str], fields: tuple[tuple[str, str], ...]) -> bool:
    return all(environment.get(key) == value for key, value in fields)


def direct_error(environment: Mapping[str, str]) -> str | None:
    if not matches(environment, DIRECT_FIELDS):
        return "Direct Standalone build control requires native-diagnostics=true, test-mode=standalone and acceptance=none."
    return None


def heap_error(environment: Mapping[str, str]) -> str | None:
    if not matches(environment, HEAP_FIELDS):
        return "Standard heap control requires native diagnostics, Standalone, acceptance=none and the Debugger arm."
    return None


def attach_error(environment: Mapping[str, str]) -> str | None:
    if not matches(environment, ATTACH_FIELDS):
        return "Direct attachment requires native Standalone diagnostics, acceptance=none, Direct disabled and standard heap disabled."
    return None


def startup_error(environment: Mapping[str, str]) -> str | None:
    if (
        attach_error(environment) is not None
        or not matches(environment, STARTUP_FIELDS)
        or environment.get("UH_NATIVE_ATTACH_AFTER_SHUTDOWN", "false") != "false"
    ):
        return "Startup-delayed attachment requires the admitted Direct attachment arm."
    return None


def shutdown_error(environment: Mapping[str, str]) -> str | None:
    if attach_error(environment) is not None or not matches(environment, SHUTDOWN_FIELDS):
        return "Shutdown-delayed attachment requires the admitted Direct attachment arm without startup selection."
    return None


def attachment_selection_error(environment: Mapping[str, str]) -> str | None:
    shutdown = environment.get("UH_NATIVE_ATTACH_AFTER_SHUTDOWN", "false")
    if shutdown == "true":
        return shutdown_error(environment)
    if shutdown != "false":
        return "Unknown attachment shutdown selection."
    startup = environment.get("UH_NATIVE_ATTACH_AFTER_STARTUP", "false")
    if startup == "true":
        return startup_error(environment)
    if startup != "false":
        return "Unknown attachment startup selection."
    return attach_error(environment)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    checks = {"direct": direct_error, "heap": heap_error, "attach": attachment_selection_error}
    parser.add_argument("selection", choices=checks)
    selection = parser.parse_args().selection
    error = checks[selection](os.environ)
    if error is not None:
        sys.stderr.buffer.write(f"{error}\n".encode())
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
