"""Validate the built-in module inventory before starting a Unity test project."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from check_runner import mapping
from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
BUILTINS = frozenset(
    [
        "ai",
        "androidjni",
        "animation",
        "assetbundle",
        "audio",
        "cloth",
        "director",
        "imageconversion",
        "imgui",
        "jsonserialize",
        "particlesystem",
        "physics",
        "physics2d",
        "screencapture",
        "terrain",
        "terrainphysics",
        "tilemap",
        "ui",
        "uielements",
        "umbra",
        "unitywebrequest",
        "unitywebrequestassetbundle",
        "unitywebrequestaudio",
        "unitywebrequesttexture",
        "unitywebrequestwww",
        "vehicles",
        "video",
        "vr",
        "wind",
        "xr",
    ]
)
BUNDLED = frozenset(("com.unity.ugui",))
PREFIX = "com.unity.modules."


def unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    """Reject repeated keys before JSON normalization can erase them."""
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def validate(value: object) -> list[str]:
    """Require real package IDs, string versions and a matching optional evidence map."""
    document = mapping(value)
    if document.get("modules") is None:
        return ["The manifest is missing a 'modules' object."]
    modules = mapping(document.get("modules"))
    if not modules:
        return ["The manifest declares no modules; the test project would fail to compile."]
    errors: list[str] = []
    for name, version in modules.items():
        if not isinstance(version, str):
            errors.append(f"Module '{name}' needs a string version.")
        elif not version.strip():
            errors.append(f"Module '{name}' has an empty version; use a nonblank string.")
        if name == "com.unity.modules.grid":
            errors.append(
                "'com.unity.modules.grid' is NOT a real package id. "
                "Use 'com.unity.modules.tilemap' instead."
            )
        elif name.startswith(PREFIX):
            if name.removeprefix(PREFIX) not in BUILTINS:
                errors.append(f"'{name}' is not a known Unity built-in module.")
        elif name not in BUNDLED:
            errors.append(f"'{name}' is not allowed here in the test-project module list.")
    evidence = document.get("_evidence")
    if evidence is not None:
        names = mapping(evidence)
        errors.extend(
            f"Module '{name}' has no matching _evidence entry."
            for name in sorted(modules.keys() - names.keys())
        )
        errors.extend(
            f"_evidence documents '{name}' but it is not in the modules list."
            for name in sorted(names.keys() - modules.keys())
        )
    return errors


def main(argv: list[str] | None = None) -> int:
    """Validate the manifest before a Unity test project starts."""
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--path", type=Path, default=ROOT / ".github/unity-test-project-modules.json"
    )
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    path: Path = args.path
    try:
        value: object = json.loads(
            path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object
        )
        errors = validate(value)
    except FileNotFoundError:
        errors = [f"Unity test-project modules manifest not found: {path}"]
    except json.JSONDecodeError as error:
        errors = [f"{path} is not valid JSON: {error}"]
    except (OSError, UnicodeError, ValueError) as error:
        errors = [f"{path}: {error}"]
    for message in errors:
        print(f"::error::{message}")
    if args.verbose or errors:
        print(f"[lint-unity-test-modules] {'FAILED' if errors else 'PASS'}: {path}")
    return int(bool(errors))


if __name__ == "__main__":
    raise SystemExit(main())
