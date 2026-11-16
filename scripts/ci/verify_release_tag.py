"""Validate release metadata before writing GitHub step outputs."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import cast

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from cli_encoding import configure_output

SEMVER = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", re.ASCII)


def verify(tag: str, source_ref: str, package_json: Path) -> tuple[str, str]:
    if not tag:
        raise ValueError("Release version is required.")
    if "\r" in tag or "\n" in tag:
        raise ValueError("Release version must be a single line.")
    if SEMVER.fullmatch(tag) is None:
        raise ValueError("Release tags must use unprefixed X.Y.Z semver.")
    if not source_ref:
        raise ValueError("Release source ref is required.")
    if "\r" in source_ref or "\n" in source_ref:
        raise ValueError("Release source ref must be a single line.")
    if not package_json.is_file():
        raise ValueError("package.json name/version is missing.")
    manifest = cast("object", json.loads(package_json.read_text(encoding="utf-8-sig")))
    if not isinstance(manifest, dict):
        raise ValueError("package.json name/version is missing.")
    values = cast("dict[str, object]", manifest)
    name = values.get("name")
    version = values.get("version")
    if not isinstance(name, str) or not name or not isinstance(version, str) or not version:
        raise ValueError("package.json name/version is missing.")
    if any(character in name or character in version for character in "\r\n\0"):
        raise ValueError("package.json name/version must be single-line strings.")
    if tag != version:
        raise ValueError(f"Tag {tag} does not match package.json version {version}.")
    return name, version


def main(arguments: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--source-ref", required=True)
    parser.add_argument("--package-json", type=Path, required=True)
    args = parser.parse_args(arguments)
    try:
        name, version = verify(args.tag, args.source_ref, args.package_json)
        output = os.environ.get("GITHUB_OUTPUT")
        if output:
            with Path(output).open("a", encoding="utf-8", newline="\n") as stream:
                stream.write(f"package-name={name}\npackage-version={version}\ntag={args.tag}\n")
        print(f"Release tag {args.tag} matches {name}@{version}.")
    except (OSError, ValueError) as error:
        print(f"::error::{error}")
        return 1
    return 0


if __name__ == "__main__":
    configure_output()
    raise SystemExit(main(sys.argv[1:]))
