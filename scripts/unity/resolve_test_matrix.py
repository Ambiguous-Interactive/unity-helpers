"""Resolve licensed Unity matrix outputs for dispatch, push, and pull requests."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import TYPE_CHECKING, NotRequired, TypedDict, cast

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent_check import changed_files, select
from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[2]
TEST_MODES = ("editmode", "playmode", "standalone")
ACCEPTANCE_VERSIONS = {"2021.3.45f1", "6000.6.0f1"}
JS_WHITESPACE = "\t\n\v\f\r \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff"
INVALID_VERSIONS = "Unity versions must be a nonempty list of distinct version strings."
SUMMARY = (
    "SINGLE_THREADED follows the selected modes. Package export smoke also runs. "
    "Unselected versions and modes are not validated by this run. Older editors run in Unity "
    "Compatibility; default manual dispatch retains full qualification.\n"
)
Exclusion = TypedDict("Exclusion", {"unity-version": str})
Matrix = TypedDict(
    "Matrix",
    {
        "unity-versions": list[str],
        "unity-exclusions": list[Exclusion],
        "test-modes": list[str],
        "coverage": NotRequired[str],
        "reason": NotRequired[str],
    },
)


def validated_versions(values: object) -> list[str]:
    if not isinstance(values, list) or not values:
        raise ValueError(INVALID_VERSIONS)
    versions: list[str] = []
    for value in cast("list[object]", values):
        if not isinstance(value, str) or not value or value.strip(JS_WHITESPACE) != value:
            raise ValueError(INVALID_VERSIONS)
        versions.append(value)
    if len(set(versions)) != len(versions):
        raise ValueError(INVALID_VERSIONS)
    return versions


def trim_input(value: object, parameter: str) -> str:
    if not isinstance(value, str):
        if value is None:
            raise ValueError("Cannot read properties of null (reading 'trim')")
        raise ValueError(f"{parameter}.trim is not a function")
    return value.strip(JS_WHITESPACE)


def resolve_test_matrix(
    values: object,
    requested_version: object = "",
    requested_mode: object = "all",
    requested_acceptance: object = "none",
) -> Matrix:
    versions = validated_versions(values)
    version = trim_input(requested_version, "requestedVersion")
    mode = trim_input(requested_mode, "requestedMode")
    if version and version not in versions:
        raise ValueError(f"Unsupported Unity version: {version}")
    if mode and mode != "all" and mode not in TEST_MODES:
        raise ValueError(f"Unsupported Unity test mode: {mode}")
    acceptance = trim_input(requested_acceptance, "requestedAcceptance")
    if (
        acceptance in {"nullableconsumer", "wproto"}
        and version
        and version not in ACCEPTANCE_VERSIONS
    ):
        label = "WProto corpus" if acceptance == "wproto" else "Nullable consumer"
        raise ValueError(
            f"{label} acceptance requires its pinned floor or latest Unity version: {version}"
        )
    selected = [version] if version else versions
    modes: list[str] = list(TEST_MODES) if not mode or mode == "all" else [mode]
    if acceptance not in {"", "none"} and "standalone" not in modes:
        modes.append("standalone")
    return {
        "unity-versions": selected,
        "unity-exclusions": [
            {"unity-version": candidate} for candidate in versions if candidate not in selected
        ],
        "test-modes": modes,
    }


def latest_version(values: object) -> str:
    latest = validated_versions(values)[-1]
    if not re.fullmatch(r"6000\.[0-9]+\.[0-9]+f[0-9]+", latest):
        raise ValueError("The blocking Unity version must be the final supported 6.x release.")
    return latest


def resolve_compatibility_matrix(values: object) -> Matrix:
    versions = validated_versions(values)
    latest = latest_version(versions)
    older = versions[:-1]
    if not older:
        raise ValueError("Compatibility coverage requires at least one older editor.")
    matrix = resolve_test_matrix(older)
    matrix["unity-exclusions"] = [{"unity-version": latest}]
    return matrix


def resolve_pull_request_matrix(values: object, files: list[str], *, root: Path = ROOT) -> Matrix:
    matrix = resolve_test_matrix(values, latest_version(values))
    proposal = select(files, "pr", root)["unity"] if files else "full"
    if proposal == "full":
        matrix["coverage"] = "latest-full-modes"
        matrix["reason"] = (
            "compatibility-sensitive or unknown input; older editors run asynchronously"
        )
    else:
        matrix["test-modes"] = ["editmode", "playmode"]
        matrix["coverage"] = "latest-editors"
        matrix["reason"] = (
            "documentation retains latest editor validation pending conditional enrollment"
            if proposal == "none"
            else "ordinary C#; latest editors, older editors run asynchronously"
        )
    return matrix


def select_matrix(versions: object, environment: Mapping[str, str], root: Path) -> Matrix:
    matrix = resolve_test_matrix(
        versions,
        environment.get("INPUT_UNITY_VERSION") or "",
        environment.get("INPUT_TEST_MODE") or "all",
        environment.get("INPUT_ACCEPTANCE") or "none",
    )
    if environment.get("INPUT_UNITY_COVERAGE") == "compatibility":
        return resolve_compatibility_matrix(versions)
    if environment.get("GITHUB_EVENT_NAME") == "push":
        return resolve_test_matrix(versions, latest_version(versions))
    if environment.get("GITHUB_EVENT_NAME") == "pull_request":
        base = environment.get("AGENT_CHECK_BASE", "")
        if not re.fullmatch(r"[0-9a-f]{40}", base):
            raise ValueError("PR matrix selection requires the immutable PR base SHA")
        return resolve_pull_request_matrix(versions, changed_files(base, root), root=root)
    return matrix


def json_line(value: object) -> str:
    encoded = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    return encoded.encode("utf-8", errors="backslashreplace").decode("utf-8")


def append_text(path: str, data: str) -> None:
    with Path(path).open("ab") as stream:
        stream.write(data.encode("utf-8"))


def publish_matrix(matrix: Matrix, environment: Mapping[str, str]) -> None:
    output = environment.get("GITHUB_OUTPUT")
    if not output:
        raise ValueError("GITHUB_OUTPUT is required to publish the Unity test matrix.")
    append_text(
        output,
        "".join(
            f"{key}={value if isinstance(value, str) else json_line(value)}\n"
            for key, value in matrix.items()
        ),
    )
    summary = environment.get("GITHUB_STEP_SUMMARY")
    if summary:
        append_text(summary, f"### Selected Unity coverage\n\n{json_line(matrix)}\n\n{SUMMARY}")


def bridge_result(payload: object) -> Matrix | str:
    if not isinstance(payload, list):
        raise ValueError("Expected a matrix bridge request.")
    arguments = cast("list[object]", payload)
    match arguments:
        case ["resolve", versions, version, mode, acceptance]:
            return resolve_test_matrix(versions, version, mode, acceptance)
        case ["latest", versions]:
            return latest_version(versions)
        case ["compatibility", versions]:
            return resolve_compatibility_matrix(versions)
        case ["pull-request", versions, files]:
            if not isinstance(files, list):
                raise ValueError("Expected repository-relative paths")
            names = cast("list[object]", files)
            if not all(isinstance(file, str) for file in names):
                raise ValueError("Expected repository-relative paths")
            return resolve_pull_request_matrix(versions, cast("list[str]", names))
        case _:
            raise ValueError("Expected a matrix bridge request.")


def main(arguments: Sequence[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--bridge", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args(arguments)
    try:
        if args.bridge:
            print(json_line(bridge_result(json.load(sys.stdin.buffer))))
        else:
            manifest: object = json.loads(
                (ROOT / ".github/unity-versions.json").read_text(encoding="utf-8")
            )
            if not isinstance(manifest, dict):
                raise ValueError(INVALID_VERSIONS)
            versions = cast("dict[str, object]", manifest).get("all")
            matrix = select_matrix(versions, os.environ, ROOT)
            publish_matrix(matrix, os.environ)
    except (OSError, ValueError) as error:
        message = str(error) if args.bridge else f"[resolve-test-matrix] {error}"
        print(message, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
