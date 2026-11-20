"""Require all run, verification, redaction, and upload outcomes for the selected Unity modes."""

from __future__ import annotations

import argparse
import json
import os
import sys
from typing import TYPE_CHECKING, cast

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

MODES = ("editmode", "playmode", "standalone")
STAGES = ("RUN", "VERIFY", "REDACT", "UPLOAD")


def selected_modes(raw: str) -> set[str]:
    payload: object = json.loads(raw)
    if isinstance(payload, str):
        entries: list[object] = [payload]
    elif isinstance(payload, list):
        entries = cast("list[object]", payload)
    else:
        raise ValueError("Selected Unity modes must be a JSON array or a single mode string.")

    names: list[str] = []
    for entry in entries:
        if not isinstance(entry, str):
            raise ValueError("Selected Unity modes must be strings.")
        names.append(entry)

    normalized = {name.lower() for name in names}
    if not names or len(normalized) != len(names):
        raise ValueError(f"Selected Unity modes must be a non-empty set: {raw}")
    unknown = [name for name in names if name.lower() not in MODES]
    if unknown:
        raise ValueError(f"Unknown selected Unity modes: {', '.join(unknown)}")
    return normalized


def assert_mode_outcomes(
    environment: Mapping[str, str], *, require_controller: bool = False
) -> None:
    """Reject missing, unexpected, or unsuccessful mode stages in report order."""
    if require_controller and environment.get("CONTROLLER") != "success":
        raise ValueError("The combined controller did not complete.")

    selected = selected_modes(environment.get("SELECTED_MODES", ""))
    failures: list[str] = []
    for mode in MODES:
        expected = "success" if mode in selected else "skipped"
        for stage in STAGES:
            actual = environment.get(f"{mode.upper()}_{stage}", "")
            if actual.lower() != expected:
                failures.append(f"{mode}/{stage}={actual} (expected {expected})")
    if failures:
        raise ValueError(f"Unity mode failures: {'; '.join(failures)}")


def main(arguments: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument(
        "--require-controller",
        action="store_true",
        help="Require a successful combined controller before judging mode outcomes.",
    )
    args = parser.parse_args(arguments)
    try:
        assert_mode_outcomes(os.environ, require_controller=args.require_controller)
    except ValueError as error:
        print(str(error).replace("\r", "\\r").replace("\n", "\\n"), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
