"""Resolve a bounded, validated editor maintenance selection without changing CI pins."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import cast

ROOT = Path(__file__).resolve().parents[2]
VERSION = re.compile(r"[1-9][0-9]{3}\.[0-9]{1,2}\.[0-9]{1,3}[abfp][1-9][0-9]{0,2}\Z")
PROFILES = ("EditorOnly", "StandaloneWindowsIl2Cpp", "Android", "Full")
MAX_VERSIONS = 16


def resolve_versions(config: Path, requested: str) -> list[str]:
    """Use canonical versions by default, or an explicit operator selection."""
    if len(requested) > 2048:
        raise ValueError("Unity version selection exceeds 2048 characters")
    if requested.strip():
        versions = re.split(r"[\s,]+", requested.strip())
    else:
        raw: object = json.loads(config.read_text(encoding="utf-8"))
        if not isinstance(raw, dict):
            raise ValueError("Unity version configuration must be an object")
        document = cast("dict[str, object]", raw)
        values = document.get("all")
        if not isinstance(values, list) or not all(
            isinstance(item, str) for item in cast("list[object]", values)
        ):
            raise ValueError("Unity version configuration must define an all[] string array")
        if not values:
            raise ValueError("Unity version configuration must define a nonempty all[] array")
        maintenance = document.get("maintenance", [])
        if not isinstance(maintenance, list) or not all(
            isinstance(item, str) for item in cast("list[object]", maintenance)
        ):
            raise ValueError(
                "Unity maintenance configuration must define a maintenance[] string array"
            )
        versions = [*cast("list[str]", values), *cast("list[str]", maintenance)]
    if not 1 <= len(versions) <= MAX_VERSIONS:
        raise ValueError(f"Select between 1 and {MAX_VERSIONS} Unity versions")
    for version in versions:
        if VERSION.fullmatch(version) is None:
            raise ValueError(f"Invalid Unity editor version: {version!r}")
    return list(dict.fromkeys(versions))


def main(argv: list[str] | None = None) -> int:
    """Emit the matrix only after all operator inputs have been validated."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=ROOT / ".github/unity-versions.json")
    parser.add_argument("--versions", default=os.environ.get("UH_MAINTENANCE_VERSIONS", ""))
    parser.add_argument(
        "--profile",
        default=os.environ.get("UH_MAINTENANCE_PROFILE", "Full"),
        choices=PROFILES,
    )
    parser.add_argument("--output", type=Path)
    args = parser.parse_args(argv)
    try:
        versions = resolve_versions(args.config, args.versions)
        matrix = json.dumps({"unity-version": versions}, separators=(",", ":"))
        outputs = f"matrix={matrix}\nprofile={args.profile}\n"
        if args.output is not None:
            with args.output.open("a", encoding="utf-8", newline="\n") as stream:
                stream.write(outputs)
        print(outputs, end="")
    except (OSError, UnicodeError, ValueError) as error:
        print(f"Cannot resolve Unity maintenance matrix: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
